variable "subscription_id" {
  description = "Azure subscription to deploy into."
  type        = string
}

variable "project" {
  description = "Short project name used in resource names."
  type        = string
  default     = "ticketing"

  validation {
    condition     = can(regex("^[a-z0-9]{2,12}$", var.project))
    error_message = "project must be 2-12 lowercase alphanumeric characters."
  }
}

variable "environment" {
  description = "Deployment environment: dev, qa, stg, or prod."
  type        = string

  validation {
    condition     = contains(["dev", "qa", "stg", "prod"], var.environment)
    error_message = "environment must be one of dev, qa, stg, prod."
  }
}

variable "app_service_sku" {
  description = "App Service plan SKU. B1 is the cheapest tier with Always On."
  type        = string
  default     = "B1"
}

variable "sql_database_sku" {
  description = "Azure SQL Database SKU. The default is serverless General Purpose, which auto-pauses when idle."
  type        = string
  default     = "GP_S_Gen5_1"
}

variable "sql_auto_pause_delay_minutes" {
  description = "Idle minutes before a serverless database pauses. -1 disables auto-pause."
  type        = number
  default     = 60
}

variable "api_client_id" {
  description = "Client ID of this environment's Entra ID app registration (created by bootstrap/). Access tokens must be issued for it."
  type        = string

  validation {
    condition     = can(regex("^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$", var.api_client_id))
    error_message = "api_client_id must be a GUID."
  }
}

variable "sql_entra_admin_login" {
  description = "Name of the Entra ID principal that administers Azure SQL. The deploy workflow passes its own identity so it can run migrations."
  type        = string
}

variable "sql_entra_admin_object_id" {
  description = "Object ID of the Entra ID SQL admin. Defaults to the identity running Terraform."
  type        = string
  default     = null
}

variable "sql_allowed_ip_addresses" {
  description = "Public IPs (e.g. your workstation) allowed through the SQL firewall for migrations and admin."
  type        = map(string)
  default     = {}
}

variable "enable_swagger" {
  description = "Expose Swagger UI outside the Development environment."
  type        = bool
  default     = false
}

variable "reservation_expiry_sweep_enabled" {
  description = "Run the background job that releases lapsed seat holds. Reservations reclaim lapsed holds on demand either way; disabling the sweep lets a serverless database auto-pause."
  type        = bool
  default     = true
}

variable "log_retention_days" {
  description = "Log Analytics retention in days."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Additional tags applied to every resource."
  type        = map(string)
  default     = {}
}
