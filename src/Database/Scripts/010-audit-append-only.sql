-- Repeatable (ADR-0017 D7): runs after every migrate, on a new or existing database.
--
-- The audit log must not be rewritable by the application. The migrator owns these tables and
-- grants the app role INSERT and SELECT by default; this revokes everything that could alter
-- history, so tampering requires direct database access rather than a compromised application.
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'eways_app') THEN
    REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_events, audit.audit_seals FROM eways_app;
  END IF;
END $$;
