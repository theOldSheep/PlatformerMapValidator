using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class StateTypeRegistry
{
    private static Dictionary<Type, int> _typeIds;

    // Call this manually at startup, or lazy load it
    public static void Initialize()
    {
        if (_typeIds != null) return;

        _typeIds = new Dictionary<Type, int>();

        // 1. Find all types in the assembly that implement IStateComponent
        var componentTypes = Assembly.GetAssembly(typeof(IStateComponent))
            .GetTypes()
            .Where(t => typeof(IStateComponent).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .OrderBy(t => t.Name) // 2. Sort alphabetically for deterministic IDs across sessions
            .ToList();

        // 3. Assign IDs (1-based, leaving 0 for "null" or special cases if needed)
        for (int i = 0; i < componentTypes.Count; i++)
        {
            _typeIds[componentTypes[i]] = i + 1;
            Debug.Log($"State Registry: Assigned ID {i + 1} to {componentTypes[i].Name}");
        }
    }

    public static int GetTypeId(object instance)
    {
        if (_typeIds == null) Initialize();
        
        Type t = instance.GetType();
        if (_typeIds.TryGetValue(t, out int id)) return id;
        
        Debug.LogWarning($"Type {t.Name} not registered.");
        return 0;
    }
}