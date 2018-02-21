// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.Rest;
using Microsoft.Rest.Azure.Authentication;

namespace MediaServicesStorageManagement
{
    /// <summary>
    /// A command line tool to manage Azure Media Services accounts and Azure Storage accounts.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            // The supported commands
            var commands = new Command[]
            {
                new SynchronizeStorageAccountKeysCommand(),
                new ListStorageAccountsCommand(),
                new ListMediaServicesAccountsCommand(),
                new ListMediaServicesAttachedStorageCommand(),
                new AttachStorageAccountCommand(),
                new DetachStorageAccountCommand()
            };

            if (args.Length == 0)
            {
                PrintUsage(commands);
                return;
            }

            var commandName = args[0];
            var command = commands.SingleOrDefault(x => string.Compare(x.Name, commandName, StringComparison.OrdinalIgnoreCase) == 0);

            if (command == null)
            {
                Console.WriteLine($"Command '{commandName}' not found");
                PrintUsage(commands);
                return;
            }

            if (args.Length - 1 != command.Parameters.Length)
            {
                Console.WriteLine("Invalid options");
                PrintUsage(new[] { command });
                return;
            }

            try
            {
                command.Run(args.Skip(1).ToArray());
            }
            catch (ApiErrorException apiErrorException)
            {
                Console.WriteLine(apiErrorException.Response.ReasonPhrase);
                Console.WriteLine(apiErrorException.Response.Content);
            }
            catch (ConfigurationErrorsException configurationErrorsException)
            {
                Console.WriteLine(configurationErrorsException.Message);
            }
        }

        private static void PrintUsage(IEnumerable<Command> commands)
        {
            Console.WriteLine("Usage:");
            foreach (var command in commands)
            {
                Console.WriteLine("  {0} {1}", command.Name, string.Join(" ", command.Parameters.Select(x => $"[{x}]").ToArray()));
            }
        }
    }

    /// <summary>
    /// A common base class for the commands supported by this tool.
    /// </summary>
    internal abstract class Command
    {
        /// <summary>
        /// Gets the command line.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Gets the command parameters.
        /// </summary>
        public abstract string[] Parameters { get; }

        /// <summary>
        /// Runs the command.
        /// </summary>
        /// <param name="parameters"></param>
        public abstract void Run(string[] parameters);

        protected static StorageManagementClient CreateStorageManagementClient()
        {
            var credentials = CreateCredentials();
            var subscriptionId = ConfigurationManager.AppSettings["SubscriptionId"];

            return new StorageManagementClient(credentials) { SubscriptionId = subscriptionId };
        }

        protected static MediaServicesManagementClient CreateMediaServicesManagementClient()
        {
            var credentials = CreateCredentials();
            var subscriptionId = ConfigurationManager.AppSettings["SubscriptionId"];

            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                throw new ConfigurationErrorsException("'SubscriptionId' must be set in the app.config file.");
            }

            return new MediaServicesManagementClient(credentials) { SubscriptionId = subscriptionId };
        }

        protected static ServiceClientCredentials CreateCredentials()
        {
            var tenantId = ConfigurationManager.AppSettings["TenantId"];
            var clientId = ConfigurationManager.AppSettings["ClientId"];
            var secret = ConfigurationManager.AppSettings["Secret"];

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new ConfigurationErrorsException("'TenantId' must be set in the app.config file.");
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ConfigurationErrorsException("'ClientId' must be set in the app.config file.");
            }

            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new ConfigurationErrorsException("'Secret' must be set in the app.config file.");
            }

            return ApplicationTokenProvider.LoginSilentAsync(tenantId, clientId, secret).Result;
        }
    }

    /// <summary>
    /// A command to update the storage account keys stored by Media Services to match those set on the Azure Storage account.
    /// </summary>
    internal class SynchronizeStorageAccountKeysCommand : Command
    {
        /// <summary>
        /// Gets the command line.
        /// </summary>
        public override string Name => "SynchronizeStorageAccountKeys";

        public override string[] Parameters => new[] { "resourceGroup", "mediaServicesAccount", "storageAccountId" };

        public override void Run(string[] parameters)
        {
            var resourceGroup = parameters[0];
            var mediaServicesAccountName = parameters[1];
            var storageAccountId = parameters[2];

            var mediaServicesManagementClient = CreateMediaServicesManagementClient();
            mediaServicesManagementClient.MediaService.SyncStorageKeys(
                resourceGroup,
                mediaServicesAccountName,
                new SyncStorageKeysInput { Id = storageAccountId });
        }
    }

    /// <summary>
    /// A command to list the Media Services accounts in a resource group.
    /// </summary>
    internal class ListMediaServicesAccountsCommand : Command
    {
        public override string Name => "ListMediaServicesAccounts";

        public override string[] Parameters => new[] { "resourceGroup" };

        public override void Run(string[] parameters)
        {
            var resourceGroup = parameters[0];

            var mediaServicesManagementClient = CreateMediaServicesManagementClient();

            foreach (var mediaServicesAccount in mediaServicesManagementClient.MediaService.ListByResourceGroup(resourceGroup))
            {
                Console.WriteLine(mediaServicesAccount.Id);
            }
        }
    }

    /// <summary>
    /// A command to list the Azure Storage accounts attached to an Azure Media Services account.
    /// </summary>
    internal class ListMediaServicesAttachedStorageCommand : Command
    {
        public override string Name => "ListMediaServicesAttachedStorage";

        public override string[] Parameters => new[] { "resourceGroup", "mediaServicesAccount" };

        public override void Run(string[] parameters)
        {
            var resourceGroup = parameters[0];
            var mediaServicesAccountName = parameters[1];

            var mediaServicesManagementClient = CreateMediaServicesManagementClient();

            var mediaServicesAccount = mediaServicesManagementClient.MediaService.Get(resourceGroup, mediaServicesAccountName);

            foreach (var storageAccount in mediaServicesAccount.StorageAccounts)
            {
                Console.WriteLine("{0}, isPrimary = {1}", storageAccount.Id, storageAccount.IsPrimary);
            }
        }
    }

    /// <summary>
    /// A command to list the Azure Storage accounts in a subscription.
    /// </summary>
    internal class ListStorageAccountsCommand : Command
    {
        public override string Name => "ListStorageAccounts";
        public override string[] Parameters => new string[0];

        public override void Run(string[] parameters)
        {
            var storageManagementClient = CreateStorageManagementClient();

            foreach (var storageAccount in storageManagementClient.StorageAccounts.List())
            {
                Console.WriteLine(storageAccount.Id);
            }
        }
    }

    /// <summary>
    /// A command to create a new Azure Storage account.
    /// </summary>
    internal class CreateStorageAccountCommand : Command
    {
        public override string Name => "CreateStorageAccount";
        public override string[] Parameters => new[] { "resourceGroup", "storageAccountName", "location" };

        public override void Run(string[] parameters)
        {
            var resourceGroup = parameters[0];
            var storageAccountName = parameters[1];
            var location = parameters[2];

            var storageManagementClient = CreateStorageManagementClient();

            var storageAccount = storageManagementClient.StorageAccounts.Create(
                resourceGroup,
                storageAccountName,
                new StorageAccountCreateParameters
                {
                    Location = location,
                    Sku = new Sku(SkuName.StandardLRS)
                });

            Console.WriteLine(storageAccount.Id);
        }
    }

    /// <summary>
    /// A command to attach an Azure Storage account to a Media Services account.
    /// </summary>
    internal class AttachStorageAccountCommand : Command
    {
        public override string Name => "AttachStorageAccount";
        public override string[] Parameters => new[] { "resourceGroup", "mediaServicesAccount", "storageAccountId" };

        public override void Run(string[] parameters)
        {
            var resourceGroup = parameters[0];
            var mediaServicesAccountName = parameters[1];
            var storageAccountId = parameters[2];

            var mediaServicesManagementClient = CreateMediaServicesManagementClient();

            var account = mediaServicesManagementClient.MediaService.Get(resourceGroup, mediaServicesAccountName);

            var updatedStorageAccounts = account.StorageAccounts.Concat(new[]
            {
                new Microsoft.Azure.Management.Media.Models.StorageAccount
                {
                    Id = storageAccountId,
                    IsPrimary = false
                }
            }).ToArray();

            var updatedAccount = new MediaService
            {
                StorageAccounts = updatedStorageAccounts
            };

            mediaServicesManagementClient.MediaService.Update(resourceGroup, mediaServicesAccountName, updatedAccount);
        }
    }

    /// <summary>
    /// A command to detach an Azure Storage account from a Media Services account.
    /// </summary>
    internal class DetachStorageAccountCommand : Command
    {
        public override string Name => "DetachStorageAccount";
        public override string[] Parameters => new[] { "resourceGroup", "mediaServicesAccount", "storageAccountId" };

        public override void Run(string[] parameters)
        {
            var resourceGroup = parameters[0];
            var mediaServicesAccountName = parameters[1];
            var storageAccountId = parameters[2];

            var mediaServicesManagementClient = CreateMediaServicesManagementClient();

            var account = mediaServicesManagementClient.MediaService.Get(resourceGroup, mediaServicesAccountName);

            var updatedStorageAccounts = account.StorageAccounts.Where(x => string.Compare(x.Id, storageAccountId, StringComparison.OrdinalIgnoreCase) != 0).ToArray();

            var updatedAccount = new MediaService
            {
                StorageAccounts = updatedStorageAccounts
            };

            mediaServicesManagementClient.MediaService.Update(resourceGroup, mediaServicesAccountName, updatedAccount);
        }
    }
}
