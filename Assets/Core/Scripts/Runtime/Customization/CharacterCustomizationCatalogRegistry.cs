using System;
using UnityEngine;

namespace Blocks.Gameplay.Core.Customization
{
    public static class CharacterCustomizationCatalogRegistry
    {
        private static CharacterCustomizationCatalog s_RuntimeCatalog;
        private static CharacterCustomizationCatalog s_CachedCatalog;

        public static event Action<CharacterCustomizationCatalog> ActiveCatalogChanged;

        public static CharacterCustomizationCatalog GetActiveCatalog()
        {
            if (s_RuntimeCatalog != null)
            {
                return s_RuntimeCatalog;
            }

            if (s_CachedCatalog != null)
            {
                return s_CachedCatalog;
            }

            s_CachedCatalog = Resources.Load<CharacterCustomizationCatalog>(CharacterCustomizationDefaults.CatalogResourcePath);
            if (s_CachedCatalog != null)
            {
                return s_CachedCatalog;
            }

            return null;
        }

        public static void SetRuntimeCatalog(CharacterCustomizationCatalog catalog)
        {
            if (ReferenceEquals(s_RuntimeCatalog, catalog))
            {
                return;
            }

            s_RuntimeCatalog = catalog;
            ActiveCatalogChanged?.Invoke(GetActiveCatalog());
        }

        public static void ClearRuntimeCatalog(CharacterCustomizationCatalog catalog = null)
        {
            if (catalog != null && !ReferenceEquals(s_RuntimeCatalog, catalog))
            {
                return;
            }

            if (s_RuntimeCatalog == null)
            {
                return;
            }

            s_RuntimeCatalog = null;
            ActiveCatalogChanged?.Invoke(GetActiveCatalog());
        }
    }
}
