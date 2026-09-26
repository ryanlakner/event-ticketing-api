output "github_repository" {
  value = var.github_repository
}

output "tenant_id" {
  value = data.azurerm_client_config.current.tenant_id
}

output "subscription_id" {
  value = data.azurerm_subscription.current.subscription_id
}

output "state_resource_group" {
  value = azurerm_resource_group.shared.name
}

output "state_storage_account" {
  value = azurerm_storage_account.tfstate.name
}

output "environments" {
  description = "Per-environment values the deploy workflow reads as GitHub environment variables."
  value = {
    for env in var.environments : env => {
      client_id         = azurerm_user_assigned_identity.deploy[env].client_id
      principal_id      = azurerm_user_assigned_identity.deploy[env].principal_id
      identity_name     = azurerm_user_assigned_identity.deploy[env].name
      resource_group    = azurerm_resource_group.environment[env].name
      api_client_id     = azuread_application.api[env].client_id
      swagger_client_id = azuread_application.swagger[env].client_id
      web_client_id     = azuread_application.web[env].client_id
      web_origins       = lookup(var.web_origins, env, [])
    }
  }
}
