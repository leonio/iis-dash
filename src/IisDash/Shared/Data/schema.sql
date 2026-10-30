-- iis-dash's database: the log files uploaded, the requests in them, and the lines that could not be read.
-- Run on every start; everything is IF NOT EXISTS.
--
-- Times (ts, first_ts, last_ts) are whole seconds since 1970-01-01 on the log's own clock, as written in the file:
-- W3C logs are written in UTC, IIS's own CSV format in the server's local time. Integers keep the range filters on an
-- index and make bucketing arithmetic (ts - ts % 900 is the quarter hour, ts % 86400 / 3600 the hour of the day).

CREATE TABLE IF NOT EXISTS log_files (
    id            INTEGER PRIMARY KEY,
    name          TEXT    NOT NULL UNIQUE,  -- uploading a file with the same name replaces it
    format        TEXT    NOT NULL,         -- W3C or IIS
    size_bytes    INTEGER NOT NULL,
    uploaded_at   TEXT    NOT NULL,         -- UTC, ISO 8601
    total_lines   INTEGER NOT NULL DEFAULT 0,
    parsed_lines  INTEGER NOT NULL DEFAULT 0,
    failed_lines  INTEGER NOT NULL DEFAULT 0,
    skipped_lines INTEGER NOT NULL DEFAULT 0, -- comments and headers
    first_ts      INTEGER,
    last_ts       INTEGER
);

CREATE TABLE IF NOT EXISTS requests (
    id               INTEGER PRIMARY KEY,
    file_id          INTEGER NOT NULL REFERENCES log_files(id) ON DELETE CASCADE,
    line_no          INTEGER NOT NULL,
    ts               INTEGER,
    method           TEXT,
    uri_stem         TEXT,
    uri_query        TEXT,
    endpoint         TEXT    NOT NULL,      -- uri_stem with ids folded and in lower case: /Orders/42 -> /orders/:id
    is_static        INTEGER NOT NULL DEFAULT 0, -- 1 for a script, style sheet, image, font... (StaticFiles.cs)
    status           INTEGER,
    sub_status       INTEGER,
    win32_status     INTEGER,
    time_taken_ms    INTEGER,
    bytes_sent       INTEGER,
    bytes_received   INTEGER,
    client_ip        TEXT,
    username         TEXT,
    user_agent       TEXT,
    referer          TEXT,
    host             TEXT,
    server_ip        TEXT,
    server_port      INTEGER,
    protocol_version TEXT,
    site_name        TEXT,
    server_name      TEXT,
    service_name     TEXT,
    cookie           TEXT
);

CREATE INDEX IF NOT EXISTS requests_ts ON requests (ts);
CREATE INDEX IF NOT EXISTS requests_file ON requests (file_id, ts);
CREATE INDEX IF NOT EXISTS requests_endpoint ON requests (endpoint, ts);
CREATE INDEX IF NOT EXISTS requests_status ON requests (status, ts);

CREATE TABLE IF NOT EXISTS failed_lines (
    file_id INTEGER NOT NULL REFERENCES log_files(id) ON DELETE CASCADE,
    line_no INTEGER NOT NULL,
    text    TEXT    NOT NULL,
    error   TEXT    NOT NULL,
    PRIMARY KEY (file_id, line_no)
);
