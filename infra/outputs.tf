output "resource_group_name" {
  value = data.azurerm_resource_group.main.name
}

output "api_app_name" {
  value = azurerm_linux_web_app.api.name
}

output "api_url" {
  value = "https://${azurerm_linux_web_app.api.default_hostname}"
}

output "api_identity_name" {
  description = "Managed identity the API uses for Azure SQL; granted access by sql/grant-app-identity.sql."
  value       = azurerm_user_assigned_identity.api.name
}

output "api_identity_client_id" {
  value = azurerm_user_assigned_identity.api.client_id
}

output "sql_server_name" {
  value = azurerm_mssql_server.main.name
}

output "sql_server_fqdn" {
  value = azurerm_mssql_server.main.fully_qualified_domain_name
}

output "sql_database_name" {
  value = azurerm_mssql_database.main.name
}

output "application_insights_name" {
  value = azurerm_application_insights.main.name
}
