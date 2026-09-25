environment      = "dev"
app_service_sku  = "B1"
sql_database_sku = "GP_S_Gen5_1"
enable_swagger   = true

# A sweep every minute would keep the serverless database awake around the clock.
# Reservations still reclaim lapsed holds on demand.
reservation_expiry_sweep_enabled = false

# sql_allowed_ip_addresses = {
#   workstation = "203.0.113.10"
# }
