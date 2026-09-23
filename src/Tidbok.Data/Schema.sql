-- Tidbok. Tider sparas som text i verksamhetens lokala tid, "yyyy-MM-ddTHH:mm",
-- vilket sorteras rätt som text och går att läsa direkt i databasen.

PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS business (
    id                  INTEGER PRIMARY KEY,
    slug                TEXT    NOT NULL UNIQUE,
    name                TEXT    NOT NULL,
    kind                TEXT    NOT NULL DEFAULT '',
    address             TEXT    NOT NULL DEFAULT '',
    phone               TEXT    NOT NULL DEFAULT '',
    accent              TEXT    NOT NULL DEFAULT '#2f6f5e',
    accent_deep         TEXT    NOT NULL DEFAULT '#1f4d41',
    slot_minutes        INTEGER NOT NULL DEFAULT 15  CHECK (slot_minutes BETWEEN 5 AND 120),
    min_notice_minutes  INTEGER NOT NULL DEFAULT 120 CHECK (min_notice_minutes BETWEEN 0 AND 10080),
    horizon_days        INTEGER NOT NULL DEFAULT 30  CHECK (horizon_days BETWEEN 1 AND 365),
    cancel_cutoff_hours INTEGER NOT NULL DEFAULT 24  CHECK (cancel_cutoff_hours BETWEEN 0 AND 336),
    closed_on_holidays  INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS service (
    id               INTEGER PRIMARY KEY,
    business_id      INTEGER NOT NULL REFERENCES business(id) ON DELETE CASCADE,
    name             TEXT    NOT NULL,
    description      TEXT    NOT NULL DEFAULT '',
    duration_minutes INTEGER NOT NULL CHECK (duration_minutes BETWEEN 5 AND 480),
    price_sek        INTEGER          CHECK (price_sek IS NULL OR price_sek >= 0),
    price_from       INTEGER NOT NULL DEFAULT 0,
    active           INTEGER NOT NULL DEFAULT 1,
    sort             INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS staff (
    id          INTEGER PRIMARY KEY,
    business_id INTEGER NOT NULL REFERENCES business(id) ON DELETE CASCADE,
    name        TEXT    NOT NULL,
    title       TEXT    NOT NULL DEFAULT '',
    active      INTEGER NOT NULL DEFAULT 1,
    sort        INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS staff_service (
    staff_id   INTEGER NOT NULL REFERENCES staff(id) ON DELETE CASCADE,
    service_id INTEGER NOT NULL REFERENCES service(id) ON DELETE CASCADE,
    PRIMARY KEY (staff_id, service_id)
);

-- Veckodag enligt .NET: 0 = söndag, 1 = måndag ... 6 = lördag. Minuter från midnatt.
CREATE TABLE IF NOT EXISTS working_hours (
    id           INTEGER PRIMARY KEY,
    staff_id     INTEGER NOT NULL REFERENCES staff(id) ON DELETE CASCADE,
    weekday      INTEGER NOT NULL CHECK (weekday BETWEEN 0 AND 6),
    start_minute INTEGER NOT NULL CHECK (start_minute BETWEEN 0 AND 1440),
    end_minute   INTEGER NOT NULL CHECK (end_minute BETWEEN 0 AND 1440),
    CHECK (end_minute > start_minute)
);

CREATE TABLE IF NOT EXISTS time_block (
    id         INTEGER PRIMARY KEY,
    staff_id   INTEGER NOT NULL REFERENCES staff(id) ON DELETE CASCADE,
    start_time TEXT    NOT NULL,
    end_time   TEXT    NOT NULL,
    reason     TEXT    NOT NULL DEFAULT '',
    CHECK (end_time > start_time)
);

CREATE TABLE IF NOT EXISTS booking (
    id             INTEGER PRIMARY KEY,
    business_id    INTEGER NOT NULL REFERENCES business(id) ON DELETE CASCADE,
    staff_id       INTEGER NOT NULL REFERENCES staff(id),
    service_id     INTEGER NOT NULL REFERENCES service(id),
    start_time     TEXT    NOT NULL,
    end_time       TEXT    NOT NULL,
    status         TEXT    NOT NULL DEFAULT 'Booked' CHECK (status IN ('Booked', 'Cancelled')),
    customer_name  TEXT    NOT NULL,
    customer_phone TEXT,
    customer_email TEXT,
    note           TEXT,
    reference      TEXT    NOT NULL UNIQUE,
    token_hash     TEXT    NOT NULL UNIQUE,
    created_at     TEXT    NOT NULL,
    cancelled_at   TEXT,
    cancelled_by   TEXT             CHECK (cancelled_by IS NULL OR cancelled_by IN ('Customer', 'Business')),
    anonymized     INTEGER NOT NULL DEFAULT 0,
    CHECK (end_time > start_time)
);

CREATE INDEX IF NOT EXISTS ix_booking_staff_start    ON booking (staff_id, start_time);
CREATE INDEX IF NOT EXISTS ix_booking_business_start ON booking (business_id, start_time);
CREATE INDEX IF NOT EXISTS ix_block_staff_start      ON time_block (staff_id, start_time);

CREATE TABLE IF NOT EXISTS app_user (
    id            INTEGER PRIMARY KEY,
    business_id   INTEGER NOT NULL REFERENCES business(id) ON DELETE CASCADE,
    email         TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    name          TEXT    NOT NULL,
    password_hash TEXT    NOT NULL
);
