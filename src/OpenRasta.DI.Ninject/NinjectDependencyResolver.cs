#region License
/* Authors:
 *      Aaron Lerch (aaronlerch@gmail.com)
 * Copyright:
 *      (C) 2007-2009 Caffeine IT & naughtyProd Ltd (http://www.caffeine-it.com)
 * License:
 *      This file is distributed under the terms of the MIT License found at the end of this file.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Ninject;
using Ninject.Activation;
using Ninject.Activation.Caching;
using Ninject.Parameters;
using Ninject.Planning;
using Ninject.Planning.Bindings;
using Ninject.Selection;
using Ninject.Selection.Heuristics;
using OpenRasta.DI.Internal;
using OpenRasta.Pipeline;
using NinjectBinding = Ninject.Planning.Bindings.Binding;

namespace OpenRasta.DI.Ninject
{
    /// <summary>
    ///   A Ninject-based <see cref = "IDependencyResolver" />.
    /// </summary>
    public class NinjectDependencyResolver : DependencyResolverCore, IDependencyResolver, IDisposable
    {
        static readonly IEnumerable<IParameter> EmptyParameters = new IParameter[] { };

        readonly IKernel _kernel;
        bool _disposeKernelOnExit;

        /// <summary>
        ///   Initializes a new instance of the <see cref = "NinjectDependencyResolver" /> class.
        /// </summary>
        public NinjectDependencyResolver() : this(null)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref = "NinjectDependencyResolver" /> class.
        /// </summary>
        /// <param name = "kernel">The kernel to use.</param>
        public NinjectDependencyResolver(IKernel kernel) : this(kernel, kernel is SubContainerKernel)
        {
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref = "NinjectDependencyResolver" /> class.
        /// </summary>
        /// <param name = "kernel">The kernel to use.</param>
        public NinjectDependencyResolver(IKernel kernel, bool disposeKernelContainerOnExit)
        {
            _kernel = kernel ?? CreateKernel();
            _disposeKernelOnExit = disposeKernelContainerOnExit;
        }

        /// <summary>
        ///   Creates an <see cref = "IKernel" /> that is configured in the way OpenRasta expects.
        /// </summary>
        /// <remarks>
        ///   OpenRasta is written with some implicit assumptions or requirements about how the
        ///   IoC container will work. For example, which constructor is selected for injection
        ///   or the fact that public "settable" properties will be injected if possible
        ///   and left alone if not possible.
        /// </remarks>
        /// <returns>A new <see cref = "IKernel" /></returns>
        public static IKernel CreateKernel()
        {
            var kernel = new StandardKernel();
            ConfigureKernel(kernel);
            return kernel;
        }

        /// <summary>
        ///   Creates an <see cref = "IKernel" /> that is configured in the way OpenRasta expects,
        ///   using the specified "parent kernel".
        /// </summary>
        /// <remarks>
        ///   OpenRasta is written with some implicit assumptions or requirements about how the
        ///   IoC container will work. For example, which constructor is selected for injection
        ///   or the fact that public "settable" properties will be injected if possible
        ///   and left alone if not possible.
        /// 
        ///   If a request to the kernel is not resolvable, the kernel will attempt to resolve the type
        ///   from the <see param = "parentKernel" />.
        /// </remarks>
        /// <returns>A new <see cref = "IKernel" /></returns>
        public static IKernel CreateKernel(IKernel parentKernel)
        {
            var kernel = new SubContainerKernel(parentKernel);
            ConfigureKernel(kernel);
            return kernel;
        }

        /// <summary>
        ///   Destructs the specified key.
        /// </summary>
        /// <param name = "key">The key.</param>
        /// <param name = "instance">The instance.</param>
        public void Destruct(string key, object instance)
        {
            var store = GetStore();
            store[key] = null;
        }

        /// <summary>
        ///   Called when an incoming request has been processed.
        /// </summary>
        public void HandleIncomingRequestProcessed()
        {
            var store = GetStore();
            store.Destruct();
        }

        /// <summary>
        ///   Determines whether the specified service type has dependency.
        /// </summary>
        /// <param name = "serviceType">Type of the service.</param>
        /// <returns>
        ///   <see langword = "true" /> if the specified service type has dependency; otherwise, <see langword = "false" />.
        /// </returns>
        public bool HasDependency(Type serviceType)
        {
            if (serviceType == null) return false;

            var bindings = GetBindings(serviceType);
            return bindings.Any();
        }

        /// <summary>
        ///   Determines whether a binding exists between the specified service and concrete types.
        /// </summary>
        public bool HasDependencyImplementation(Type serviceType, Type concreteType)
        {
            if (serviceType == null || concreteType == null) return false;

            if (serviceType == concreteType)
            {
                return HasDependency(serviceType);
            }

            var bindings = GetBindings(serviceType);
            var request = _kernel.CreateRequest(serviceType, null, EmptyParameters, false, false);
            return bindings.Any(b =>
            {
                if (b.Target != BindingTarget.Type) return false;
                var context = new Context(_kernel,
                                          request,
                                          b,
                                          _kernel.Components.Get<ICache>(),
                                          _kernel.Components.Get<IPlanner>(),
                                          _kernel.Components.Get<global::Ninject.Activation.IPipeline>());
                return b.GetProvider(context).Type == concreteType;
            });
        }

        public void Dispose()
        {
            if (_disposeKernelOnExit) _kernel.Dispose();
        }

        /// <summary>
        ///   Adds the dependency.
        /// </summary>
        /// <param name = "concreteType">Type of the concrete class to create.</param>
        /// <param name = "lifetime">The lifetime of the registration.</param>
        protected override void AddDependencyCore(Type concreteType, DependencyLifetime lifetime)
        {
            AddDependencyCore(concreteType, concreteType, lifetime);
        }

        /// <summary>
        ///   Adds the dependency.
        /// </summary>
        /// <param name = "serviceType">Type of the service to bind to.</param>
        /// <param name = "concreteType">Type of the concrete class to create.</param>
        /// <param name = "lifetime">The lifetime of the registration.</param>
        protected override void AddDependencyCore(Type serviceType, Type concreteType, DependencyLifetime lifetime)
        {
            var binding = CreateBinding(serviceType, lifetime);
            if (lifetime == DependencyLifetime.PerRequest)
            {
                binding.ProviderCallback =
                    ctx =>
                    new PerRequestProvider(concreteType,
                                           ctx.Kernel.Components.Get<IPlanner>(),
                                           ctx.Kernel.Components.Get<ISelector>());
                binding.Target = BindingTarget.Provider;
            }
            else
            {
                var bindingBuilder = new BindingBuilder<object>(binding, _kernel);
                var bindingScope = bindingBuilder.To(concreteType);
                if (lifetime == DependencyLifetime.Singleton)
                    bindingScope.InSingletonScope();
            }

            _kernel.AddBinding(binding);
        }

        /// <summary>
        ///   Adds the an instance to the dependencies.
        /// </summary>
        /// <param name = "serviceType">Type of the service to add.</param>
        /// <param name = "instance">The instance of the service to add.</param>
        /// <param name = "lifetime">The lifetime for the registration.</param>
        protected override void AddDependencyInstanceCore(Type serviceType, object instance, DependencyLifetime lifetime)
        {
            if (lifetime == DependencyLifetime.Transient) return;

            var binding = _kernel.GetBindings(serviceType).FirstOrDefault();
            bool foundExistingBinding = (binding != null);
            if (binding == null)
            {
                binding = CreateBinding(serviceType, lifetime);
                _kernel.AddBinding(binding);
            }

            var builder = new BindingBuilder<object>(binding, _kernel);
            if (lifetime == DependencyLifetime.PerRequest)
            {
                if (foundExistingBinding && binding.Target != BindingTarget.Method)
                {
                    // A binding exists, but wasn't specified as an instance callback. Error!
                    throw new DependencyResolutionException(
                        string.Format(
                            "Cannot register an instance for type '{0}' because there is already a binding target of type '{1}'.",
                            serviceType.Name,
                            binding.Target));
                }

                var store = GetStore();
                var key = serviceType.GetKey();
                store[key] = instance;

                if (!foundExistingBinding)
                {
                    store.GetContextInstances().Add(new ContextStoreDependency(key,
                                                                               instance,
                                                                               new ContextStoreDependencyCleaner(_kernel)));
                }

                builder.ToMethod(c =>
                {
                    var ctxStore = GetStore();
                    return ctxStore[serviceType.GetKey()];
                });
            }
            else if (lifetime == DependencyLifetime.Singleton)
            {
                builder.ToConstant(instance).InSingletonScope();
            }
        }

        /// <summary>
        ///   Resolves all the specified types.
        /// </summary>
        /// <typeparam name = "TService">The type of the service.</typeparam>
        /// <returns></returns>
        protected override IEnumerable<TService> ResolveAllCore<TService>()
        {
            Type serviceType = typeof(TService);
            return _kernel.GetAll<TService>();
        }

        /// <summary>
        ///   Resolves an instance of the <see cref = "IKernel" />.
        /// </summary>
        /// <param name = "serviceType">Type of the service.</param>
        /// <returns></returns>
        protected override object ResolveCore(Type serviceType)
        {
            RequireDependancy(serviceType);
            return _kernel.Get(serviceType);
        }

        static void ConfigureKernel(IKernel kernel)
        {
            // Needed to support OpenRasta's assumptions.
            kernel.Components.Add<IInjectionHeuristic, AllResolvablePropertiesInjectionHeuristic>();
            kernel.Components.RemoveAll(typeof(IConstructorScorer));
            kernel.Components.Add<IConstructorScorer, InjectableConstructorScorer>();
        }

        static IBinding CreateBinding(Type serviceType, DependencyLifetime lifetime)
        {
            return (lifetime == DependencyLifetime.PerRequest)
                       ? new WebBinding(serviceType)
                       : new NinjectBinding(serviceType);
        }

        static bool IsWebInstance(IBinding binding)
        {
            return (binding is WebBinding) && (binding.Target == BindingTarget.Method);
        }

        IEnumerable<IBinding> GetBindings(Type service)
        {
            return from binding in _kernel.GetBindings(service)
                   where IsAvailable(binding)
                   select binding;
        }

        IContextStore GetStore()
        {
            return _kernel.Get<IContextStore>();
        }

        bool IsAvailable(IBinding binding)
        {
            if (IsWebInstance(binding))
            {
                if (!HasDependency(typeof(IContextStore))) return false;
                var store = GetStore();
                bool isInstanceAvailable = store[binding.Service.GetKey()] != null;
                return isInstanceAvailable;
            }

            return binding.Target != BindingTarget.Method;
        }

        void RequireDependancy(Type serviceType)
        {
            if (!HasDependency(serviceType))
            {
                throw new DependencyResolutionException("Unable to resolve dependency for {0}".With(serviceType));
            }
        }
    }
}

#region Full license
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
#endregion