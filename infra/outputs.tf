output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "api_app_name" {
  value = azurerm_linux_web_app.api.name
}

output "api_url" {
  value = "https://${azurerm_linux_web_app.api.default_hostname}"
}

output "api_principal_id" {
  description = "Managed identity of the API; grant it database access with sql/grant-app-identity.sql."
  value       = azurerm_linux_web_app.api.identity[0].principal_id
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
