resource "azurerm_service_plan" "main" {
  name                = "asp-${local.name}"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = var.app_service_sku
  tags                = local.tags
}

# User-assigned (rather than system-assigned) so the identity outlives the web app and its
# client ID is known to Terraform. The deploy workflow uses that ID to create the database
# user WITH SID, which avoids granting Azure SQL Microsoft Graph permissions.
resource "azurerm_user_assigned_identity" "api" {
  name                = "id-${local.name}-api"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  tags                = local.tags
}

resource "azurerm_linux_web_app" "api" {
  # bootstrap/identity.tf derives Swagger UI's sign-in redirect URI from this name.
  name                = "app-${local.name}-api"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  service_plan_id     = azurerm_service_plan.main.id
  https_only          = true
  tags                = local.tags

  # The API authenticates to Azure SQL with this identity (see sql/grant-app-identity.sql).
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api.id]
  }

  site_config {
    always_on                         = !contains(["F1", "D1"], var.app_service_sku)
    ftps_state                        = "Disabled"
    http2_enabled                     = true
    minimum_tls_version               = "1.2"
    health_check_path                 = "/health"
    health_check_eviction_time_in_min = 5

    application_stack {
      dotnet_version = "10.0"
    }
  }

  app_settings = merge({
    ASPNETCORE_ENVIRONMENT                = local.aspnetcore_environment
    APPLICATIONINSIGHTS_CONNECTION_STRING = azurerm_application_insights.main.connection_string
    AZURE_CLIENT_ID                       = azurerm_user_assigned_identity.api.client_id
    Swagger__Enabled                      = tostring(var.enable_swagger)
    Reservations__ExpirySweepEnabled      = tostring(var.reservation_expiry_sweep_enabled)

    # Access tokens: Entra ID for this tenant, issued for this environment's app registration.
    # v2 tokens carry the client ID as the audience; the api:// URI is accepted too.
    Authentication__Schemes__Bearer__Authority         = local.entra_authority
    Authentication__Schemes__Bearer__ValidIssuer       = local.entra_authority
    Authentication__Schemes__Bearer__ValidAudiences__0 = var.api_client_id
    Authentication__Schemes__Bearer__ValidAudiences__1 = "api://${var.api_client_id}"
  }, local.swagger_sign_in_settings)

  # Surfaces to the app as ConnectionStrings:Database.
  connection_string {
    name  = "Database"
    type  = "SQLAzure"
    value = "Server=tcp:${azurerm_mssql_server.main.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.main.name};Authentication=Active Directory Managed Identity;User Id=${azurerm_user_assigned_identity.api.client_id};Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;"
  }

  logs {
    detailed_error_messages = false
    failed_request_tracing  = false

    http_logs {
      file_system {
        retention_in_days = 7
        retention_in_mb   = 35
      }
    }
  }
}

resource "azurerm_monitor_diagnostic_setting" "api" {
  name                       = "diag-${local.name}-api"
  target_resource_id         = azurerm_linux_web_app.api.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  enabled_log {
    category = "AppServiceHTTPLogs"
  }

  enabled_log {
    category = "AppServiceConsoleLogs"
  }

  enabled_log {
    category = "AppServiceAppLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
