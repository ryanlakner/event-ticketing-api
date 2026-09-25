resource "azurerm_service_plan" "main" {
  name                = "asp-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = var.app_service_sku
  tags                = local.tags
}

resource "azurerm_linux_web_app" "api" {
  name                = "app-${local.name}-api"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  service_plan_id     = azurerm_service_plan.main.id
  https_only          = true
  tags                = local.tags

  # The API authenticates to Azure SQL with this identity (see sql/grant-app-identity.sql).
  identity {
    type = "SystemAssigned"
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

  app_settings = {
    ASPNETCORE_ENVIRONMENT                = local.aspnetcore_environment
    APPLICATIONINSIGHTS_CONNECTION_STRING = azurerm_application_insights.main.connection_string
    Swagger__Enabled                      = tostring(var.enable_swagger)
    Reservations__ExpirySweepEnabled      = tostring(var.reservation_expiry_sweep_enabled)
  }

  # Surfaces to the app as ConnectionStrings:Database.
  connection_string {
    name  = "Database"
    type  = "SQLAzure"
    value = "Server=tcp:${azurerm_mssql_server.main.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.main.name};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;"
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
