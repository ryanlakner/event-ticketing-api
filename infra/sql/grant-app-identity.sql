-- Grants the API's managed identity read/write access to the application database.
-- Idempotent: the deploy workflow runs it on every deployment, after migrations.
--
-- The user is created WITH SID (derived from the identity's client ID) instead of
-- FROM EXTERNAL PROVIDER, so Azure SQL never needs Microsoft Graph permissions to look it up.
--
-- sqlcmd -S <server>.database.windows.net -d <database> --authentication-method ActiveDirectoryAzCli \
--   -v AppIdentityName="id-ticketing-dev-api" -v AppIdentityClientId="<client id>" -i grant-app-identity.sql
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppIdentityName)')
BEGIN
    DECLARE @sid NVARCHAR(100) = CONVERT(
        NVARCHAR(100),
        CONVERT(VARBINARY(16), CAST(N'$(AppIdentityClientId)' AS UNIQUEIDENTIFIER)),
        1
    );
    DECLARE @sql NVARCHAR(400) =
        N'CREATE USER ' + QUOTENAME(N'$(AppIdentityName)') + N' WITH SID = ' + @sid + N', TYPE = E;';
    EXEC sp_executesql @sql;
    PRINT N'Created database user $(AppIdentityName).';
END;

ALTER ROLE db_datareader ADD MEMBER [$(AppIdentityName)];
ALTER ROLE db_datawriter ADD MEMBER [$(AppIdentityName)];
