data "azurerm_client_config" "current" {}

data "azurerm_subscription" "current" {}

locals {
  tags = {
    project    = var.project
    managed_by = "terraform-bootstrap"
  }
}

# --- Terraform remote state ----------------------------------------------------

resource "azurerm_resource_group" "shared" {
  name     = "rg-${var.project}-shared"
  location = var.location
  tags     = local.tags
}

resource "azurerm_storage_account" "tfstate" {
  # Storage names are globally unique; derive a stable suffix from the subscription.
  name                            = "st${var.project}${substr(sha1(data.azurerm_subscription.current.id), 0, 8)}"
  resource_group_name             = azurerm_resource_group.shared.name
  location                        = azurerm_resource_group.shared.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  shared_access_key_enabled       = false
  allow_nested_items_to_be_public = false
  tags                            = local.tags

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 14
    }
  }
}

resource "azurerm_storage_container" "tfstate" {
  name               = "tfstate"
  storage_account_id = azurerm_storage_account.tfstate.id
}

# Lets whoever runs the bootstrap read state locally too (e.g. `terraform plan`).
resource "azurerm_role_assignment" "operator_state" {
  scope                = azurerm_storage_account.tfstate.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

# --- Per-environment deploy identity -----------------------------------------------

resource "azurerm_resource_group" "environment" {
  for_each = var.environments

  name     = "rg-${var.project}-${each.key}"
  location = var.location
  tags     = merge(local.tags, { environment = each.key })
}

resource "azurerm_user_assigned_identity" "deploy" {
  for_each = var.environments

  name                = "id-${var.project}-${each.key}-deploy"
  resource_group_name = azurerm_resource_group.shared.name
  location            = azurerm_resource_group.shared.location
  tags                = merge(local.tags, { environment = each.key })
}

# Trusts GitHub's OIDC tokens, but only for jobs bound to this repository's environment.
resource "azurerm_federated_identity_credential" "github" {
  for_each = var.environments

  name                      = "github-${each.key}"
  user_assigned_identity_id = azurerm_user_assigned_identity.deploy[each.key].id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  subject                   = "repo:${var.github_repository}:environment:${each.key}"
}

# Least privilege: the pipeline manages resources inside its own resource group only.
resource "azurerm_role_assignment" "deploy_contributor" {
  for_each = var.environments

  scope                = azurerm_resource_group.environment[each.key].id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.deploy[each.key].principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "deploy_state" {
  for_each = var.environments

  scope                = azurerm_storage_account.tfstate.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.deploy[each.key].principal_id
  principal_type       = "ServicePrincipal"
}
