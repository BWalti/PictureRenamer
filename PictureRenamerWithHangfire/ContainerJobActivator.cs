namespace PictureRenamerWithHangfire
{
    using System;

    using Hangfire;

    using Microsoft.Extensions.DependencyInjection;

    public class ContainerJobActivator : JobActivator
    {
        private readonly IServiceProvider container;

        public ContainerJobActivator(IServiceProvider container)
        {
            this.container = container;
        }

        public override object ActivateJob(Type type)
        {
            return this.container.GetRequiredService(type);
        }
    }
}