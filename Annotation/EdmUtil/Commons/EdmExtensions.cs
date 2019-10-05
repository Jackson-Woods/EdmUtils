﻿
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Validation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Annotation.EdmUtil.Commons
{
    public static class EdmExtensions
    {
        public static IEdmProperty ResolveProperty(this IEdmStructuredType type, string propertyName, bool enableCaseInsensitive = false)
        {
            IEdmProperty property = type.FindProperty(propertyName);
            if (property != null || !enableCaseInsensitive)
            {
                return property;
            }

            var result = type.Properties()
            .Where(_ => string.Equals(propertyName, _.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

            return result.SingleOrDefault();
        }

        public static IEdmSchemaType ResolveType(this IEdmModel model, string typeName, bool enableCaseInsensitive = false)
        {
            IEdmSchemaType type = model.FindType(typeName);
            if (type != null || !enableCaseInsensitive)
            {
                return type;
            }

            var types = model.SchemaElements.OfType<IEdmSchemaType>()
                .Where(e => string.Equals(typeName, e.FullName(), enableCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

            foreach (var refModels in model.ReferencedModels)
            {
                var refedTypes = refModels.SchemaElements.OfType<IEdmSchemaType>()
                    .Where(e => string.Equals(typeName, e.FullName(), enableCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

                types = types.Concat(refedTypes);
            }

            if (types.Count() > 1)
            {
                throw new Exception($"Multiple type found from the model for '{typeName}'.");
            }

            return types.SingleOrDefault();
        }

        /// <summary>
        /// Resolve the navigation source using the input identifier
        /// </summary>
        /// <param name="model">The Edm model.</param>
        /// <param name="identifier">The indentifier</param>
        /// <param name="enableCaseInsensitive">Enable case insensitive</param>
        /// <returns>Null or the found navigation source.</returns>
        public static IEdmNavigationSource ResolveNavigationSource(this IEdmModel model, string identifier, bool enableCaseInsensitive = false)
        {
            IEdmNavigationSource navSource = model.FindDeclaredNavigationSource(identifier);
            if (navSource != null || !enableCaseInsensitive)
            {
                return navSource;
            }

            IEdmEntityContainer container = model.EntityContainer;
            if (container == null)
            {
                return null;
            }

            var result = container.Elements.OfType<IEdmNavigationSource>()
                .Where(source => string.Equals(identifier, source.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            if (result.Count > 1)
            {
                throw new Exception($"More than one navigation sources match the name '{identifier}' found in model.");
            }

            return result.SingleOrDefault();
        }

        public static IEnumerable<IEdmOperationImport> ResolveOperationImports(this IEdmModel model,
            string identifier,
            bool enableCaseInsensitive = false)
        {
            IEnumerable<IEdmOperationImport> results = model.FindDeclaredOperationImports(identifier);
            if (results.Any() || !enableCaseInsensitive)
            {
                return results;
            }

            IEdmEntityContainer container = model.EntityContainer;
            if (container == null)
            {
                return null;
            }

            return container.Elements.OfType<IEdmOperationImport>()
                .Where(source => string.Equals(identifier, source.Name, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<IEdmOperation> ResolveOperations(this IEdmModel model, string identifier,
            IEdmType bindingType, bool enableCaseInsensitive = false)
        {
            IEnumerable<IEdmOperation> results;
            if (identifier.Contains("."))
            {
                results = FindAcrossModels<IEdmOperation>(model, identifier, true, enableCaseInsensitive);
            }
            else
            {
                results = FindAcrossModels<IEdmOperation>(model, identifier, false, enableCaseInsensitive);
            }

            var operations = results?.ToList();
            if (operations != null && operations.Count() > 0)
            {
                IList<IEdmOperation> matchedOperation = new List<IEdmOperation>();
                for (int i = 0; i < operations.Count(); i++)
                {
                    if (operations[i].HasEquivalentBindingType(bindingType))
                    {
                        matchedOperation.Add(operations[i]);
                    }
                }

                return matchedOperation;
            }

            return Enumerable.Empty<IEdmOperation>();
        }

        internal static IEdmEntitySetBase GetTargetEntitySet(this IEdmOperation operation, IEdmNavigationSource source, IEdmModel model)
        {
            if (source == null)
            {
                return null;
            }

            if (operation.IsBound && operation.Parameters.Any())
            {
                IEdmOperationParameter parameter;
                Dictionary<IEdmNavigationProperty, IEdmPathExpression> path;
                IEdmEntityType lastEntityType;

                if (operation.TryGetRelativeEntitySetPath(model, out parameter, out path, out lastEntityType, out IEnumerable<EdmError>  _))
                {
                    IEdmNavigationSource target = source;

                    foreach (var navigation in path)
                    {
                        target = target.FindNavigationTarget(navigation.Key, navigation.Value);
                    }

                    return target as IEdmEntitySetBase;
                }
            }

            return null;
        }


        public static IEdmNavigationSource FindNavigationTarget(this IEdmNavigationSource navigationSource,
            IEdmNavigationProperty navigationProperty, IList<PathSegment> parsedSegments, out IEdmPathExpression bindingPath)
        {
            bindingPath = null;

            if (navigationProperty.ContainsTarget)
            {
                return navigationSource;
                // return navigationSource.FindNavigationTarget(navigationProperty);
            }

            IEnumerable<IEdmNavigationPropertyBinding> bindings =
                navigationSource.FindNavigationPropertyBindings(navigationProperty);

            if (bindings != null)
            {
                foreach (var binding in bindings)
                {
                    if (BindingPathHelper.MatchBindingPath(binding.Path, parsedSegments))
                    {
                        bindingPath = binding.Path;
                        return binding.Target;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<T> FindAcrossModels<T>(IEdmModel model,
            string identifier, bool fullName, bool caseInsensitive) where T : IEdmSchemaElement
        {
            Func<IEdmModel, IEnumerable<T>> finder = (refModel) =>
                refModel.SchemaElements.OfType<T>()
                .Where(e => string.Equals(identifier, fullName ? e.FullName() : e.Name,
                caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

            IEnumerable<T> results = finder(model);

            foreach (IEdmModel reference in model.ReferencedModels)
            {
                results.Concat(finder(reference));
            }

            return results;
        }

        internal static bool IsStructuredCollectionType(this IEdmTypeReference typeReference)
        {
            return typeReference.Definition.IsStructuredCollectionType();
        }

        internal static bool IsStructuredCollectionType(this IEdmType type)
        {
            IEdmCollectionType collectionType = type as IEdmCollectionType;

            if (collectionType == null
                || (collectionType.ElementType != null
                    && (collectionType.ElementType.TypeKind() != EdmTypeKind.Entity && collectionType.ElementType.TypeKind() != EdmTypeKind.Complex)))
            {
                return false;
            }

            return true;
        }

        public static bool IsEntityCollectionType(this IEdmType edmType, out IEdmEntityType entityType)
        {
            if (edmType == null || edmType.TypeKind != EdmTypeKind.Collection)
            {
                entityType = null;
                return false;
            }

            entityType = ((IEdmCollectionType)edmType).ElementType.Definition as IEdmEntityType;
            return entityType != null;
        }

        public static bool IsEntityOrEntityCollectionType(this IEdmType edmType, out IEdmEntityType entityType)
        {
            if (edmType.TypeKind == EdmTypeKind.Entity)
            {
                entityType = (IEdmEntityType)edmType;
                return true;
            }

            if (edmType.TypeKind != EdmTypeKind.Collection)
            {
                entityType = null;
                return false;
            }

            entityType = ((IEdmCollectionType)edmType).ElementType.Definition as IEdmEntityType;
            return entityType != null;
        }
    }
}
