#!/usr/bin/env bash
# Creates the GitHub environments and copies bootstrap outputs into their variables.
# Run after `terraform apply` in this directory, with `gh` logged in as a repo admin.
# None of these values are secrets: OIDC federation means no credentials are stored.
set -euo pipefail
cd "$(dirname "$0")"

repo=$(terraform output -raw github_repository)
tenant_id=$(terraform output -raw tenant_id)
subscription_id=$(terraform output -raw subscription_id)
state_rg=$(terraform output -raw state_resource_group)
state_account=$(terraform output -raw state_storage_account)

for env in $(terraform output -json environments | jq -r 'keys[]'); do
  echo "Configuring GitHub environment '$env' on $repo"
  gh api --method PUT "repos/$repo/environments/$env" --silent

  values=$(terraform output -json environments | jq -r --arg env "$env" '.[$env]')
  set_var() { gh variable set "$1" --env "$env" --repo "$repo" --body "$2"; }

  set_var AZURE_TENANT_ID "$tenant_id"
  set_var AZURE_SUBSCRIPTION_ID "$subscription_id"
  set_var AZURE_CLIENT_ID "$(jq -r .client_id <<<"$values")"
  set_var DEPLOY_IDENTITY_NAME "$(jq -r .identity_name <<<"$values")"
  set_var DEPLOY_IDENTITY_PRINCIPAL_ID "$(jq -r .principal_id <<<"$values")"
  set_var TF_STATE_RESOURCE_GROUP "$state_rg"
  set_var TF_STATE_STORAGE_ACCOUNT "$state_account"
done

# The deploy workflow is skipped until this is set, so it never fails before bootstrap.
gh variable set DEPLOY_ENABLED --repo "$repo" --body true

echo "Done. For prod, add required reviewers under Settings → Environments → prod."
