variable "subscription_id" {
  description = "Azure subscription to deploy into."
  type        = string
}

variable "project" {
  description = "Short project name; must match the main configuration."
  type        = string
  default     = "ticketing"
}

variable "location" {
  description = "Azure region for all resource groups."
  type        = string
  default     = "eastus2"
}

variable "github_repository" {
  description = "GitHub repository (owner/name) allowed to deploy."
  type        = string
  default     = "ryanlakner/event-ticketing-api"
}

variable "environments" {
  description = "Environments to prepare. Each gets its own resource group, deploy identity, and GitHub environment."
  type        = set(string)
  default     = ["dev", "qa", "stg", "prod"]

  validation {
    condition     = alltrue([for env in var.environments : contains(["dev", "qa", "stg", "prod"], env)])
    error_message = "environments may only contain dev, qa, stg, and prod."
  }
}

variable "operator_role_environments" {
  description = "Environments where whoever runs the bootstrap is granted both app roles, so they can try the API straight away."
  type        = set(string)
  default     = ["dev", "qa"]
}

variable "web_origins" {
  description = "Deployed origins of the web app per environment, e.g. { dev = [\"https://tickets-dev.example.com\"] }. Used for sign-in redirects and the API's CORS allow-list."
  type        = map(list(string))
  default     = {}

  validation {
    condition     = alltrue([for origins in values(var.web_origins) : alltrue([for o in origins : can(regex("^https://[^/]+$", o))])])
    error_message = "web_origins must be https origins with no path or trailing slash."
  }
}
