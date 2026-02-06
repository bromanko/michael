env "local" {
  src = "file://schema.sql"
  dev = "sqlite://dev?mode=memory"

  migration {
    dir = "file://migrations"
  }
}
