-- Execute only in the dedicated telemetry database as its provisioning owner.
-- Runtime role receives DML privileges separately; it cannot create/drop tables.
CREATE TABLE telemetry_schema (version integer PRIMARY KEY CHECK (version = 1));
INSERT INTO telemetry_schema VALUES (1);
CREATE TABLE events (seq bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  "eventKey" text UNIQUE NOT NULL, timestamp text NOT NULL, "receivedAt" text NOT NULL,
  service text NOT NULL, "instanceId" text NOT NULL, kind text NOT NULL, level text NOT NULL,
  "traceId" text, "diagnosticId" text, "queryId" text, "requestId" text, "companyId" text,
  "bridgeId" text, body text NOT NULL, bytes integer NOT NULL CHECK (bytes > 0));
CREATE INDEX event_time ON events(timestamp,seq);
CREATE INDEX event_trace ON events("traceId");
CREATE INDEX event_request ON events("requestId","companyId","bridgeId");
CREATE TABLE meta (key text PRIMARY KEY, value jsonb NOT NULL);
CREATE TABLE producers (producer text NOT NULL, service text NOT NULL, "instanceId" text NOT NULL,
  "receivedAt" text NOT NULL, dropped bigint NOT NULL, PRIMARY KEY(producer,service,"instanceId"));
CREATE TABLE audit (seq bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, at text NOT NULL,
  actor jsonb NOT NULL, action text NOT NULL, target text NOT NULL, outcome text NOT NULL, detail text);
