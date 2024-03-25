using System;
using System.Collections.Generic;
using System.Linq;

namespace Clpsplug.PooledObjects.Runtime
{
    /// <summary>
    /// Poolable objects must implement this interface.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>
        /// If this instance is used, set this to true.
        /// </summary>
        bool isUsed { get; }
    }

    /// <summary>
    /// Behaviour when <see cref="PooledObjects{T}"/> exhausts instances in its pool.
    /// </summary>
    public enum ExhaustionBehaviour
    {
        /// <summary>
        /// Do not expand the pool. Instead, throw <see cref="PooledObjectsExhaustedException{T}"/>.
        /// </summary>
        Throw,

        /// <summary>
        /// Return null or default (in case of struct). Be careful when using this behaviour.
        /// </summary>
        NullOrDefault,

        /// <summary>
        /// Add only one instance to the pool and use it.
        /// </summary>
        AddOne,

        /// <summary>
        /// Double the amount of the instance.
        /// </summary>
        Double,
    }

    public abstract class PooledObjectsBase
    {
        /// <summary>
        /// Base of base class for inspector (not used in production at all.)
        /// </summary>
        private protected PooledObjectsBase()
        {
#if UNITY_EDITOR
            PoolInfoGatherer.RegisterInstance(this);
#endif
        }

        /// <summary>
        /// Total instance count. Can change depending on <see cref="ExhaustionBehaviour"/>.
        /// </summary>
        public abstract int instanceCount { get; }

        /// <summary>
        /// What happens if the pool exhausts its available instances?
        /// </summary>
        public abstract ExhaustionBehaviour ExhaustionBehaviour { get; protected set; }


        /// <summary>
        /// Currently available instance count.
        /// </summary>
        public abstract int AvailableInstances { get; }
    }

    /// <summary>
    /// Pooled objects. Can be component or plain-old class instance.
    /// These components are made at Start and then pooled as inactive,
    /// then becomes active when they are needed.
    /// After it's not used anymore, it will become inactive again
    /// and reset its state.
    /// (This is the common implementation - use <see cref="PooledObjects{T}"/> etc.)
    /// </summary>
    public abstract class PooledObjectsBase<T> : PooledObjectsBase where T : IPoolable
    {
        private readonly List<T> _pool = new List<T>();

        public override int instanceCount => _pool.Count;
        public override ExhaustionBehaviour ExhaustionBehaviour { get; protected set; }
        private int NextInstanceId { get; set; }

        private Func<T> _instantiationCode;

        private readonly object _lock = new object();

        /// <summary>
        /// Initialises this pool.
        /// </summary>
        /// <param name="instantiationCode">Put function that creates an instance. This is most likely a factory.</param>
        /// <param name="instanceCountAtStart">Initial instance count.</param>
        /// <param name="exhaustionBehaviour">Behaviour when the pool is exhausted (see <see cref="ExhaustionBehaviour"/></param>
        public void Initialize(
            Func<T> instantiationCode,
            int instanceCountAtStart,
            ExhaustionBehaviour exhaustionBehaviour = ExhaustionBehaviour.Throw
        )
        {
            if (instanceCountAtStart <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instanceCountAtStart),
                    "Instance count must at least be 1.");
            }

            _instantiationCode = instantiationCode;
            ExhaustionBehaviour = exhaustionBehaviour;
            if (_pool.Count > 0)
            {
                foreach (var item in _pool)
                {
                    OnDestroy(item);
                }
            }

            _pool.Clear();
            for (var i = 0; i < instanceCountAtStart; i++)
            {
                _pool.Add(_instantiationCode.Invoke());
            }

            foreach (var item in _pool)
            {
                OnCreate(item);
            }
        }

        /// <summary>
        /// Try to get an instance from pool.
        /// Depending on <see cref="ExhaustionBehaviour"/>, it throws, or it creates more instances.
        /// </summary>
        /// <returns>
        /// A ready-to-use item, or in case <see cref="ExhaustionBehaviour"/> is
        /// <see cref="ExhaustionBehaviour.NullOrDefault"/>, it might return null if the pool is exhausted.
        /// </returns>
        /// <exception cref="PooledObjectsExhaustedException{T}">
        /// When <see cref="ExhaustionBehaviour"/> is set to <see cref="ExhaustionBehaviour.Throw"/>
        /// and all the instances in the pool are used (i.e., <see cref="IPoolable.isUsed"/> == true).
        /// </exception>
        /// <exception cref="PoolNotInitialisedException{T}">
        /// When you forgot to initialise this pool.
        /// Ensure that you run <see cref="Initialize"/> before spawning an instance.
        /// </exception>
        protected T TrySpawn()
        {
            if (_pool.Count == 0)
            {
                throw new PoolNotInitialisedException<T>();
            }

            // Make this part thread safe
            lock (_lock)
            {
                // We use index directly instead of foreach to even load out.
                var startingInstanceId = NextInstanceId;
                if (_pool.Count == 1)
                {
                    // Edge case - if there is only one instance pooled, the code in else block can crash.
                    if (!_pool[0].isUsed) return _pool[0];
                }
                else
                {
                    // This for should iterate over all elements at least once.
                    for (var i = startingInstanceId; i < instanceCount + startingInstanceId; i++)
                    {
                        var item = _pool[i % instanceCount];
                        if (item.isUsed) continue;
                        NextInstanceId = i + 1;
                        NextInstanceId %= instanceCount;
                        return item;
                    }
                }

                switch (ExhaustionBehaviour)
                {
                    case ExhaustionBehaviour.Throw:
                        throw new PooledObjectsExhaustedException<T>();
                    case ExhaustionBehaviour.NullOrDefault:
                        return default;
                    case ExhaustionBehaviour.AddOne:
                    {
                        var item = _instantiationCode.Invoke();
                        OnCreate(item);
                        _pool.Add(item);
                        return item;
                    }
                    case ExhaustionBehaviour.Double:
                    {
                        var addedInstances = new List<T>();
                        for (var i = 0; i < instanceCount; i++)
                        {
                            addedInstances.Add(_instantiationCode.Invoke());
                        }

                        foreach (var item in addedInstances)
                        {
                            OnCreate(item);
                        }

                        _pool.AddRange(addedInstances);
                        // ReSharper disable once TailRecursiveCall
                        return TrySpawn();
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        /// <summary>
        /// Explicitly despawn an item.
        /// The <see cref="item"/> should set its <see cref="IPoolable.isUsed"/> to false.
        /// An item can signal itself despawned by setting <see cref="IPoolable.isUsed"/> to false on its own,
        /// in which case the pool will reuse that item later on.
        /// </summary>
        /// <param name="item"></param>
        public void Despawn(T item)
        {
            OnDespawn(item);
        }

        /// <summary>
        /// Explicitly destroys <b>ALL</b> items.
        /// To use this pool again, you will need to run <see cref="Initialize"/> again.
        /// </summary>
        public void Destroy()
        {
            foreach (var item in _pool)
            {
                OnDestroy(item);
            }

            NextInstanceId = 0;
            _pool.Clear();
        }

        /// <summary>
        /// Called when an instance is created (i.e., <see cref="Initialize"/>.)
        /// The component should initialise its state.
        /// You might want to set the instance inactive here.
        /// </summary>
        /// <param name="item">Item being created for the first time.</param>
        protected virtual void OnCreate(T item)
        {
            /* no-op */
        }

        /// <summary>
        /// Called when an instance is no longer used.
        /// </summary>
        /// <param name="item">Despawning item</param>
        protected virtual void OnDespawn(T item)
        {
            /* no-op */
        }

        /// <summary>
        /// Called when the entire pool is destroyed.
        /// </summary>
        /// <param name="item">Object being destroyed</param>
        protected virtual void OnDestroy(T item)
        {
            /* no-op */
        }

        public override int AvailableInstances => _pool.Count(i => !i.isUsed);
    }

    public abstract class PooledObjects<TItem> : PooledObjectsBase<TItem> where TItem : IPoolable
    {
        /// <summary>
        /// Spawn an instance.
        /// </summary>
        /// <returns>
        /// A ready-to-use item, or in case the exhaustion behaviour is <see cref="ExhaustionBehaviour.NullOrDefault"/>,
        /// it might return null if the pool is exhausted.
        /// </returns>
        /// <exception cref="PooledObjectsExhaustedException{T}">
        /// When the exhaustion behaviour is set to <see cref="ExhaustionBehaviour.Throw"/>
        /// and all the instances in the pool are used (i.e., <see cref="IPoolable.isUsed"/> == true).
        /// </exception>
        /// <exception cref="PoolNotInitialisedException{T}">
        /// When you forgot to initialise this pool.
        /// Ensure that you run <see cref="PooledObjectsBase{T}.Initialize"/> before spawning an instance.
        /// </exception>
        public TItem Spawn()
        {
            var item = TrySpawn();
            OnSpawn(item);
            return item;
        }

        /// <summary>
        /// Called when an instance is called for use.
        /// </summary>
        /// <param name="item"></param>
        protected virtual void OnSpawn(TItem item)
        {
            /* no-op */
        }
    }

    public abstract class PooledObjects<T1, TItem> : PooledObjectsBase<TItem> where TItem : IPoolable
    {
        /// <inheritdoc cref="PooledObjects{T}.Spawn"/>
        public TItem Spawn(T1 param1)
        {
            var item = TrySpawn();
            OnSpawn(item, param1);
            return item;
        }

        /// <summary>
        /// Called when an instance is called for use.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="param1"></param>
        protected virtual void OnSpawn(TItem item, T1 param1)
        {
            /* no-op */
        }
    }

    public abstract class PooledObjects<T1, T2, TItem> : PooledObjectsBase<TItem> where TItem : IPoolable
    {
        /// <inheritdoc cref="PooledObjects{T}.Spawn"/>
        public TItem Spawn(T1 param1, T2 param2)
        {
            var item = TrySpawn();
            OnSpawn(item, param1, param2);
            return item;
        }

        /// <summary>
        /// Called when an instance is called for use.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="param1"></param>
        /// <param name="param2"></param>
        protected virtual void OnSpawn(TItem item, T1 param1, T2 param2)
        {
            /* no-op */
        }
    }
}