terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.7"
    }
  }

  # Partial configuration: supply values with `terraform init -backend-config=backend.hcl`.
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id

  # Registering resource providers needs subscription-level rights, which the deploy identity
  # deliberately lacks. bootstrap/ runs as an Owner and registers them instead.
  resource_provider_registrations = "none"
}
