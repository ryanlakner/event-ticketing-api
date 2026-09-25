# Azure SQL with Entra ID-only authentication: no SQL passwords exist anywhere.
resource "azurerm_mssql_server" "main" {
  name                          = "sql-${local.name}"
  resource_group_name           = data.azurerm_resource_group.main.name
  location                      = data.azurerm_resource_group.main.location
  version                       = "12.0"
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true
  tags                          = local.tags

  azuread_administrator {
    login_username              = var.sql_entra_admin_login
    object_id                   = coalesce(var.sql_entra_admin_object_id, data.azurerm_client_config.current.object_id)
    tenant_id                   = data.azurerm_client_config.current.tenant_id
    azuread_authentication_only = true
  }

  identity {
    type = "SystemAssigned"
  }
}

resource "azurerm_mssql_database" "main" {
  name                        = "sqldb-${local.name}"
  server_id                   = azurerm_mssql_server.main.id
  sku_name                    = var.sql_database_sku
  max_size_gb                 = 32
  min_capacity                = startswith(var.sql_database_sku, "GP_S_") ? 0.5 : null
  auto_pause_delay_in_minutes = startswith(var.sql_database_sku, "GP_S_") ? var.sql_auto_pause_delay_minutes : null
  zone_redundant              = false
  storage_account_type        = "Local"
  tags                        = local.tags
}

# The special 0.0.0.0 rule allows traffic from Azure services (including App Service).
# For stricter isolation, replace with VNet integration + a private endpoint.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_firewall_rule" "allowed" {
  for_each = var.sql_allowed_ip_addresses

  name             = each.key
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = each.value
  end_ip_address   = each.value
}
