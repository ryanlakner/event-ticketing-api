data "azurerm_client_config" "current" {}

locals {
  name = "${var.project}-${var.environment}"

  tags = merge(var.tags, {
    project     = var.project
    environment = var.environment
    managed_by  = "terraform"
  })

  # Never "Development" in Azure: that name turns on local-only behaviour such as migrating
  # on startup. Each environment gets its own name so appsettings.{Name}.json can target it.
  # Entra ID v2 endpoint for this tenant: tokens are issued by, and signing keys published at, it.
  entra_authority = "https://login.microsoftonline.com/${data.azurerm_client_config.current.tenant_id}/v2.0"
  entra_oauth     = "https://login.microsoftonline.com/${data.azurerm_client_config.current.tenant_id}/oauth2/v2.0"

  cors_settings = {
    for index, origin in var.cors_allowed_origins : "Cors__AllowedOrigins__${index}" => origin
  }

  # Swagger UI's "Authorize" button signs in with Entra ID, wherever Swagger is exposed.
  swagger_sign_in_settings = var.enable_swagger && var.swagger_client_id != "" ? {
    Swagger__SignIn__ClientId         = var.swagger_client_id
    Swagger__SignIn__AuthorizationUrl = "${local.entra_oauth}/authorize"
    Swagger__SignIn__TokenUrl         = "${local.entra_oauth}/token"
    Swagger__SignIn__Scope            = "api://${var.api_client_id}/access_as_user"
  } : {}

  aspnetcore_environment = {
    dev  = "Dev"
    qa   = "QA"
    stg  = "Staging"
    prod = "Production"
  }[var.environment]
}

# Created by bootstrap/, so the deploy identity only needs Contributor on this group.
data "azurerm_resource_group" "main" {
  name = "rg-${local.name}"
}

# --- Observability -----------------------------------------------------------

resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.name}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = local.tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${local.name}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.tags
}
