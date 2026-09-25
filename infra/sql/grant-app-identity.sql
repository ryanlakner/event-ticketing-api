-- Run once per environment against the application database, connected as the Entra SQL admin.
-- Replace app-ticketing-dev-api with the `api_app_name` Terraform output.
CREATE USER [app-ticketing-dev-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [app-ticketing-dev-api];
ALTER ROLE db_datawriter ADD MEMBER [app-ticketing-dev-api];
-- Only needed if Database__ApplyMigrationsOnStartup=true for this environment.
-- ALTER ROLE db_ddladmin ADD MEMBER [app-ticketing-dev-api];
