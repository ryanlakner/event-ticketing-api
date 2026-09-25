terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.7"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.10"
    }
  }

  # Local state on purpose: this runs once, by a subscription Owner (who can also create Entra ID
  # app registrations), before remote state exists.
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id

  # The state account disables shared keys, so data-plane calls must use Entra ID.
  storage_use_azuread = true

  # The deploy identity can't register resource providers (that needs subscription scope),
  # so register everything the main configuration uses here, as an Owner.
  resource_provider_registrations = "core"
  resource_providers_to_register = [
    "Microsoft.Insights",
    "Microsoft.OperationalInsights",
    "Microsoft.Sql",
    "Microsoft.Web",
  ]
}

# Uses the tenant of the signed-in Azure CLI account.
provider "azuread" {}
