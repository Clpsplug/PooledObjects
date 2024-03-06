using System.Collections.Generic;
using UnityEngine;

namespace Clpsplug.PooledObjects.Runtime
{
#if UNITY_EDITOR
    public static class PoolInfoGatherer
    {
        private static readonly List<PooledObjectsBase> Instances
            = new List<PooledObjectsBase>();
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Instances.Clear();
        }

        public static void RegisterInstance(PooledObjectsBase instance)
        {
            Instances.Add(instance);
        }

        public static List<PooledObjectsBase> GetInstances()
        {
            return Instances;
        }
    }
#endif
}