ALTER TABLE bookings ADD COLUMN cancellation_token TEXT;

CREATE UNIQUE INDEX idx_bookings_cancellation_token
    ON bookings (cancellation_token)
    WHERE cancellation_token IS NOT NULL;
