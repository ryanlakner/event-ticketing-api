# Functional testing. Dev-sized to keep costs low, but with the expiry sweep on so testers
# see reservations move to Expired on schedule, as they will in production.
environment      = "qa"
app_service_sku  = "B1"
sql_database_sku = "GP_S_Gen5_1"
enable_swagger   = true

reservation_expiry_sweep_enabled = true
