using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdvancedLoader
{
    internal static class UiCache
    {
        private static readonly Dictionary<Component, CanvasGroup[]> Groups = new Dictionary<Component, CanvasGroup[]>();
        private static readonly Dictionary<Component, Canvas> Canvases = new Dictionary<Component, Canvas>();
        private static readonly Dictionary<Transform, Dictionary<string, Transform>> Children = new Dictionary<Transform, Dictionary<string, Transform>>();

        public static CanvasGroup[] GroupsAbove(Component component)
        {
            if (!Groups.TryGetValue(component, out var groups))
            {
                Prune(Groups);
                Groups[component] = groups = component.GetComponentsInParent<CanvasGroup>(true);
            }
            return groups;
        }

        public static Canvas CanvasOf(Component component)
        {
            if (!Canvases.TryGetValue(component, out var canvas) || canvas == null)
            {
                Prune(Canvases);
                Canvases[component] = canvas = component.GetComponentInParent<Canvas>();
            }
            return canvas;
        }

        public static Transform Child(Transform parent, string name)
        {
            if (!Children.TryGetValue(parent, out var children))
            {
                Prune(Children);
                Children[parent] = children = new Dictionary<string, Transform>();
            }
            if (!children.TryGetValue(name, out var child) || child == null) children[name] = child = parent.Find(name);
            return child;
        }

        // Destroyed keys compare equal to null
        private static void Prune<TKey, TValue>(Dictionary<TKey, TValue> cache) where TKey : Object
        {
            if (cache.Count < 64) return;
            foreach (var key in cache.Keys.Where(x => x == null).ToList()) cache.Remove(key);
        }
    }
}
