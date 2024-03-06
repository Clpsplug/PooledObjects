using System;

namespace Clpsplug.PooledObjects.Runtime
{
    public abstract class PooledObjectException : Exception
    { }

    /// <summary>
    /// There are no more pooled objects left
    /// and you specified to NOT expand the pool on exhaustion/
    /// </summary>
    /// <typeparam name="T">Type of pooled objects</typeparam>
    public class PooledObjectsExhaustedException<T> : PooledObjectException
    {
        public override string Message =>
            $"Memory pool for {typeof(T)} has no more instance ready to be used! Check if isUsed is properly controlled and/or demand of the instances";
    }

    /// <summary>
    /// Attempted to spawn an object but initialisation hasn't run yet.
    /// </summary>
    /// <typeparam name="T">Type of pooled objects</typeparam>
    public class PoolNotInitialisedException<T> : PooledObjectException
    {
        public override string Message =>
            $"Memory pool for {typeof(T)} is not initialised!!!";
    }
}