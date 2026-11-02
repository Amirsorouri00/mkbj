-- Repeatable (ADR-0017 D7): runs after every migrate, on a new or existing database.
--
-- The storefront views are refreshed through catalog.refresh_storefront_view, which the
-- StorefrontViewsV1 migration creates. Refreshing requires owning the view and eways_app owns
-- nothing, so the function is SECURITY DEFINER and runs as the migrator. Who may call it is a
-- grant, and grants live here rather than in a migration.
-- A SECURITY DEFINER function is executable by PUBLIC unless that is taken away.
REVOKE ALL ON FUNCTION catalog.refresh_storefront_view(text, boolean) FROM PUBLIC;

DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'eways_app') THEN
    GRANT EXECUTE ON FUNCTION catalog.refresh_storefront_view(text, boolean) TO eways_app;
  END IF;
END $$;
