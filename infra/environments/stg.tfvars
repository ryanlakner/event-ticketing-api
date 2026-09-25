# Pre-production. Mirrors prod's SKUs and settings so releases are proven on the same
# configuration first; the only difference is that the database may pause when idle.
environment                  = "stg"
app_service_sku              = "P0v3"
sql_database_sku             = "GP_S_Gen5_2"
sql_auto_pause_delay_minutes = 60
enable_swagger               = false

reservation_expiry_sweep_enabled = true
