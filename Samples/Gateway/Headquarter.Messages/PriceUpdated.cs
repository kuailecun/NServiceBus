﻿namespace Headquarter.Messages
{
    using System;
    using NServiceBus;

    [Serializable]
    public class PriceUpdated : IMessage
    {
        public int ProductId { get; set; }
        public double NewPrice { get; set; }
        public DateTime ValidFrom { get; set; }
    }
}