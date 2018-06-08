﻿using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Azure.ServiceBus.Primitives;

namespace Microsoft.Azure.ServiceBus.Management
{
    public class SubscriptionDescription : IEquatable<SubscriptionDescription>
    {
        string topicPath, subscriptionName;
        TimeSpan lockDuration = TimeSpan.FromSeconds(60);
        long maxSizeInGB = 1;
        TimeSpan defaultMessageTimeToLive = TimeSpan.MaxValue;
        TimeSpan autoDeleteOnIdle = TimeSpan.MaxValue;
        TimeSpan duplicateDetectionHistoryTimeWindow = TimeSpan.FromSeconds(30);
        int maxDeliveryCount = 10;
        string forwardTo = null;
        string forwardDeadLetteredMessagesTo = null;

        public SubscriptionDescription(string topicPath, string subscriptionName)
        {
            this.TopicPath = topicPath;
            this.SubscriptionName = subscriptionName;
        }

        public TimeSpan LockDuration
        {
            get => this.lockDuration;
            set
            {
                TimeoutHelper.ThrowIfNonPositiveArgument(value, nameof(LockDuration));
                this.lockDuration = value;
            }
        }

        public bool RequiresSession { get; set; } = false;

        public TimeSpan DefaultMessageTimeToLive
        {
            get => this.defaultMessageTimeToLive;
            set
            {
                if (value < ManagementConstants.MinimumAllowedTimeToLive || value > ManagementConstants.MaximumAllowedTimeToLive)
                {
                    throw new ArgumentOutOfRangeException(nameof(DefaultMessageTimeToLive),
                        $"The value must be between {ManagementConstants.MinimumAllowedTimeToLive} and {ManagementConstants.MaximumAllowedTimeToLive}");
                }

                this.defaultMessageTimeToLive = value;
            }
        }

        public TimeSpan AutoDeleteOnIdle
        {
            get => this.autoDeleteOnIdle;
            set
            {
                if (value < ManagementConstants.MinimumAllowedAutoDeleteOnIdle)
                {
                    throw new ArgumentOutOfRangeException(nameof(AutoDeleteOnIdle),
                        $"The value must be greater than {ManagementConstants.MinimumAllowedAutoDeleteOnIdle}");
                }

                this.autoDeleteOnIdle = value;
            }
        }

        public bool EnableDeadLetteringOnMessageExpiration { get; set; } = false;

        public bool EnableDeadLetteringOnFilterEvaluationExceptions { get; set; } = true;

        public string TopicPath
        {
            get => this.topicPath;
            set
            {
                ManagementClient.CheckValidTopicName(value, nameof(TopicPath));
                this.topicPath = value;
            }
        }

        public string SubscriptionName
        {
            get => this.subscriptionName;
            set
            {
                ManagementClient.CheckValidSubscriptionName(value, nameof(SubscriptionName));
                this.subscriptionName = value;
            }
        }

        public int MaxDeliveryCount
        {
            get => this.maxDeliveryCount;
            set
            {
                if (value < ManagementConstants.MinAllowedMaxDeliveryCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(MaxDeliveryCount),
                        $"The value must be greater than {ManagementConstants.MinAllowedMaxDeliveryCount}");
                }

                this.maxDeliveryCount = value;
            }
        }

        public EntityStatus Status { get; set; } = EntityStatus.Active;

        public string ForwardTo
        {
            get => this.forwardTo;
            set
            {
                ManagementClient.CheckValidQueueName(value, nameof(ForwardTo));
                if (this.topicPath.Equals(value, StringComparison.CurrentCultureIgnoreCase))
                {
                    throw new InvalidOperationException("Entity cannot have auto-forwarding policy to itself");
                }

                this.forwardTo = value;
            }
        }

        public string ForwardDeadLetteredMessagesTo
        {
            get => this.forwardDeadLetteredMessagesTo;
            set
            {
                ManagementClient.CheckValidQueueName(value, nameof(ForwardDeadLetteredMessagesTo));
                if (this.topicPath.Equals(value, StringComparison.CurrentCultureIgnoreCase))
                {
                    throw new InvalidOperationException("Entity cannot have auto-forwarding policy to itself");
                }

                this.forwardDeadLetteredMessagesTo = value;
            }
        }

        static internal SubscriptionDescription ParseFromContent(string topicName, string xml)
        {
            var xDoc = XElement.Parse(xml);
            if (!xDoc.IsEmpty)
            {
                if (xDoc.Name.LocalName == "entry")
                {
                    return ParseFromEntryElement(topicName, xDoc);
                }
            }

            throw new MessagingEntityNotFoundException("Subscription was not found");
        }

        static internal IList<SubscriptionDescription> ParseCollectionFromContent(string topicName, string xml)
        {
            var xDoc = XElement.Parse(xml);
            if (!xDoc.IsEmpty)
            {
                if (xDoc.Name.LocalName == "feed")
                {
                    var subscriptionList = new List<SubscriptionDescription>();

                    var entryList = xDoc.Elements(XName.Get("entry", ManagementConstants.AtomNs));
                    foreach (var entry in entryList)
                    {
                        subscriptionList.Add(ParseFromEntryElement(topicName, entry));
                    }

                    return subscriptionList;
                }
            }

            throw new MessagingEntityNotFoundException("Subscription was not found");
        }

        // TODO: Authorization
        static private SubscriptionDescription ParseFromEntryElement(string topicName, XElement xEntry)
        {
            try
            {
                var name = xEntry.Element(XName.Get("title", ManagementConstants.AtomNs)).Value;
                var subscriptionDesc = new SubscriptionDescription(topicName, name);

                var qdXml = xEntry.Element(XName.Get("content", ManagementConstants.AtomNs))?
                    .Element(XName.Get("SubscriptionDescription", ManagementConstants.SbNs));

                if (qdXml == null)
                {
                    throw new MessagingEntityNotFoundException("Subscription was not found");
                }

                foreach (var element in qdXml.Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "RequiresSession":
                            subscriptionDesc.RequiresSession = bool.Parse(element.Value);
                            break;
                        case "DeadLetteringOnMessageExpiration":
                            subscriptionDesc.EnableDeadLetteringOnMessageExpiration = bool.Parse(element.Value);
                            break;
                        case "DeadLetteringOnFilterEvaluationExceptions":
                            subscriptionDesc.EnableDeadLetteringOnFilterEvaluationExceptions = bool.Parse(element.Value);
                            break;
                        case "LockDuration":
                            subscriptionDesc.LockDuration = XmlConvert.ToTimeSpan(element.Value);
                            break;
                        case "DefaultMessageTimeToLive":
                            subscriptionDesc.DefaultMessageTimeToLive = XmlConvert.ToTimeSpan(element.Value);
                            break;
                        case "MaxDeliveryCount":
                            subscriptionDesc.MaxDeliveryCount = int.Parse(element.Value);
                            break;
                        case "Status":
                            subscriptionDesc.Status = (EntityStatus)Enum.Parse(typeof(EntityStatus), element.Value);
                            break;
                        case "AutoDeleteOnIdle":
                            subscriptionDesc.AutoDeleteOnIdle = XmlConvert.ToTimeSpan(element.Value);
                            break;
                    }
                }

                return subscriptionDesc;
            }
            catch (Exception ex) when (!(ex is ServiceBusException))
            {
                throw new ServiceBusException(false, ex);
            }
        }

        // TODO: Authorization rules
        internal XDocument Serialize()
        {
            XDocument doc = new XDocument(
                new XElement(XName.Get("entry", ManagementConstants.AtomNs),
                    new XElement(XName.Get("content", ManagementConstants.AtomNs),
                        new XAttribute("type", "application/xml"),
                        new XElement(XName.Get("SubscriptionDescription", ManagementConstants.SbNs),
                            new XElement(XName.Get("LockDuration", ManagementConstants.SbNs), XmlConvert.ToString(this.LockDuration)),
                            new XElement(XName.Get("RequiresSession", ManagementConstants.SbNs), XmlConvert.ToString(this.RequiresSession)),
                            this.DefaultMessageTimeToLive != TimeSpan.MaxValue ? new XElement(XName.Get("DefaultMessageTimeToLive", ManagementConstants.SbNs), XmlConvert.ToString(this.DefaultMessageTimeToLive)) : null,
                            new XElement(XName.Get("DeadLetteringOnMessageExpiration", ManagementConstants.SbNs), XmlConvert.ToString(this.EnableDeadLetteringOnMessageExpiration)),
                            new XElement(XName.Get("DeadLetteringOnFilterEvaluationExceptions", ManagementConstants.SbNs), XmlConvert.ToString(this.EnableDeadLetteringOnFilterEvaluationExceptions)),
                            new XElement(XName.Get("MaxDeliveryCount", ManagementConstants.SbNs), XmlConvert.ToString(this.MaxDeliveryCount)),
                            new XElement(XName.Get("Status", ManagementConstants.SbNs), this.Status.ToString()),
                            this.ForwardTo != null ? new XElement(XName.Get("ForwardTo", ManagementConstants.SbNs), this.ForwardTo) : null,
                            this.ForwardDeadLetteredMessagesTo != null ? new XElement(XName.Get("ForwardDeadLetteredMessagesTo", ManagementConstants.SbNs), this.ForwardDeadLetteredMessagesTo) : null,
                            this.AutoDeleteOnIdle != TimeSpan.MaxValue ? new XElement(XName.Get("AutoDeleteOnIdle", ManagementConstants.SbNs), XmlConvert.ToString(this.AutoDeleteOnIdle)) : null
                        ))
                    ));

            return doc;
        }

        public bool Equals(SubscriptionDescription other)
        {
            if (this.SubscriptionName.Equals(other.SubscriptionName, StringComparison.OrdinalIgnoreCase)
                && this.TopicPath.Equals(other.TopicPath, StringComparison.OrdinalIgnoreCase)
                && this.AutoDeleteOnIdle.Equals(other.AutoDeleteOnIdle)
                && this.DefaultMessageTimeToLive.Equals(other.DefaultMessageTimeToLive)
                && this.EnableDeadLetteringOnMessageExpiration == other.EnableDeadLetteringOnMessageExpiration
                && this.EnableDeadLetteringOnFilterEvaluationExceptions == other.EnableDeadLetteringOnFilterEvaluationExceptions
                && string.Equals(this.ForwardDeadLetteredMessagesTo, other.ForwardDeadLetteredMessagesTo, StringComparison.OrdinalIgnoreCase)
                && string.Equals(this.ForwardTo, other.ForwardTo, StringComparison.OrdinalIgnoreCase)
                && this.LockDuration.Equals(other.LockDuration)
                && this.MaxDeliveryCount == other.MaxDeliveryCount
                && this.RequiresSession.Equals(other.RequiresSession)
                && this.Status.Equals(other.Status))
            {
                return true;
            }

            return false;
        }
    }
}
