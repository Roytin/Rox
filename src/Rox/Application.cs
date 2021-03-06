﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.Logging;
using System.Linq;
using Microsoft.Extensions.Hosting;

namespace Rox
{
    public class Application
    {
        private readonly Dictionary<string, ModuleBase> _modules = new Dictionary<string, ModuleBase>();
        private IServiceCollection _services;
        private IConfiguration _configuration;

        public Application()
        {
        }

        public void Start(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>(_modules.Count);
            using (var provider = _services.BuildServiceProvider())
            {
                var context = new ApplicationInitializationContext(provider);
                foreach (var module in _modules.Values)
                {
                    //_logger.LogTrace($"程序启动前预热模块: {module.GetType().Name}");
                    tasks.Add(module.PreApplicationInitialization(context, cancellationToken));
                }
                Task.WhenAll(tasks).Wait();

                tasks.Clear();
                foreach (var module in _modules.Values)
                {
                    //_logger.LogTrace($"程序启动完初始化模块: {module.GetType().Name}");
                    tasks.Add(module.OnApplicationInitialization(context, cancellationToken));
                }

                Task.WhenAll(tasks).Wait();
            }
        }
        public void Configure(IServiceCollection services, IConfiguration configuration, CancellationToken cancellationToken)
        {
            _services = services;
            _configuration = configuration;

            var tasks = new List<Task>(_modules.Count);
            foreach (var module in _modules.Values)
            {
                tasks.Add(module.ConfigureServices(new ServicesConfigureContext(_services, _configuration), cancellationToken));
            }
            Task.WhenAll(tasks).Wait();
        }

        public void Init<TModule>(IHostBuilder builder) where TModule : ModuleBase, new()
        {
            //反射注册modules
            var type = typeof(TModule);
            FindDependencies(type);
            //_logger.LogInformation($"模块组: {string.Join(",", _modules.Values.Select(x=>x.GetType().Name))}");
            foreach (var module in _modules.Values)
            {
                //_logger.LogTrace($"模块配置: {module.GetType().Name}");
                module.ConfigureHost(builder);
            }
        }

        public void Stop(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>(_modules.Count); 
            using var sp = _services.BuildServiceProvider();
            foreach (var module in _modules.Values)
            {
                tasks.Add(module.OnStopping(new ApplicationShutdownContext(sp), cancellationToken));
            }
            Task.WhenAll(tasks).Wait();
        }

        private void FindDependencies(Type type)
        {
            //throw new InvalidOperationException($"Type {type.FullName} can't depends on type {type.FullName}, becasuse it's in dead cycle!");
            var attrs = type.GetCustomAttributes<DependencyAttribute>();
            foreach (var attr in attrs)
            {
                foreach (var t in attr.DenpendsOnTypes)
                {
                    if (!_modules.ContainsKey(t.FullName))
                    {
                        //_modules.Add(t.FullName, Activator.CreateInstance(t) as ModuleBase);
                        FindDependencies(t);
                    }
                }
            }
            _modules.Add(type.FullName, Activator.CreateInstance(type) as ModuleBase);
        }
    }

}
