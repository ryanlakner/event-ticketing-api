# --- Entra ID app registration for the API -------------------------------------------
#
# One registration per environment, so a token issued for dev is never accepted by prod.
# Users are assigned the Organizer or Customer app role; the role appears in the token's
# "roles" claim, which the API's [Authorize(Roles = ...)] checks read.

data "azuread_client_config" "current" {}

locals {
  # Fixed IDs so roles and the scope keep their identity (and assignments) across applies.
  app_roles = {
    Organizer = {
      id          = "16a79133-7d25-4ea7-bdf8-299b7323b4aa"
      description = "Create, publish, and manage their own events."
    }
    Customer = {
      id          = "5147ef50-9c16-4bbf-905c-f6f83c6d311d"
      description = "Reserve, confirm, and cancel tickets."
    }
  }
  access_scope_id = "3decfed2-d4da-4f62-95c7-5e5e6b8c48c0"

  # Microsoft's well-known Azure CLI client, pre-authorized so developers can get tokens with
  # `az account get-access-token --scope api://<client id>/access_as_user`.
  azure_cli_client_id = "04b07795-8ddb-461a-bbee-02f9e1bf7b46"

  operator_role_assignments = {
    for pair in setproduct(
      setintersection(var.environments, var.operator_role_environments),
      keys(local.app_roles)
    ) : "${pair[0]}-${pair[1]}" => { environment = pair[0], role = pair[1] }
  }
}

resource "azuread_application" "api" {
  for_each = var.environments

  display_name     = "${var.project}-api-${each.key}"
  sign_in_audience = "AzureADMyOrg"
  owners           = [data.azuread_client_config.current.object_id]

  api {
    # v2 tokens: issuer https://login.microsoftonline.com/<tenant>/v2.0, audience = client ID.
    requested_access_token_version = 2

    oauth2_permission_scope {
      id                         = local.access_scope_id
      value                      = "access_as_user"
      type                       = "User"
      enabled                    = true
      admin_consent_display_name = "Access the ticketing API"
      admin_consent_description  = "Call the event ticketing API as the signed-in user."
      user_consent_display_name  = "Access the ticketing API"
      user_consent_description   = "Call the event ticketing API on your behalf."
    }
  }

  dynamic "app_role" {
    for_each = local.app_roles

    content {
      id                   = app_role.value.id
      value                = app_role.key
      display_name         = app_role.key
      description          = app_role.value.description
      allowed_member_types = ["User"]
      enabled              = true
    }
  }

  # The identifier URI embeds the client ID, so it is set by a separate resource below.
  lifecycle {
    ignore_changes = [identifier_uris]
  }
}

resource "azuread_application_identifier_uri" "api" {
  for_each = var.environments

  application_id = azuread_application.api[each.key].id
  identifier_uri = "api://${azuread_application.api[each.key].client_id}"
}

resource "azuread_application_pre_authorized" "azure_cli" {
  for_each = var.environments

  application_id       = azuread_application.api[each.key].id
  authorized_client_id = local.azure_cli_client_id
  permission_ids       = [local.access_scope_id]
}

resource "azuread_service_principal" "api" {
  for_each = var.environments

  client_id = azuread_application.api[each.key].client_id
  owners    = [data.azuread_client_config.current.object_id]
}

resource "azuread_app_role_assignment" "operator" {
  for_each = local.operator_role_assignments

  app_role_id         = local.app_roles[each.value.role].id
  principal_object_id = data.azuread_client_config.current.object_id
  resource_object_id  = azuread_service_principal.api[each.value.environment].object_id
}

# --- Swagger UI sign-in ---------------------------------------------------------------
#
# A separate public (SPA) client per environment: Swagger UI signs users in with the
# authorization code flow + PKCE, so no client secret exists. It is pre-authorized for the
# API's scope, so users aren't asked to consent.

locals {
  swagger_redirect_uris = {
    for env in var.environments : env => concat(
      # Must match the web app name in infra/app_service.tf ("app-<project>-<env>-api").
      ["https://app-${var.project}-${env}-api.azurewebsites.net/swagger/oauth2-redirect.html"],
      # Local development on the ports in Ticketing.Api/Properties/launchSettings.json.
      env == "dev" ? [
        "http://localhost:5084/swagger/oauth2-redirect.html",
        "https://localhost:7109/swagger/oauth2-redirect.html",
      ] : []
    )
  }
}

resource "azuread_application" "swagger" {
  for_each = var.environments

  display_name     = "${var.project}-swagger-${each.key}"
  sign_in_audience = "AzureADMyOrg"
  owners           = [data.azuread_client_config.current.object_id]

  single_page_application {
    redirect_uris = local.swagger_redirect_uris[each.key]
  }

  required_resource_access {
    resource_app_id = azuread_application.api[each.key].client_id

    resource_access {
      id   = local.access_scope_id
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "swagger" {
  for_each = var.environments

  client_id = azuread_application.swagger[each.key].client_id
  owners    = [data.azuread_client_config.current.object_id]
}

resource "azuread_application_pre_authorized" "swagger" {
  for_each = var.environments

  application_id       = azuread_application.api[each.key].id
  authorized_client_id = azuread_application.swagger[each.key].client_id
  permission_ids       = [local.access_scope_id]
}
