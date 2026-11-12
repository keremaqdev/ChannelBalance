-- Inventory Sync Engine MVP schema (PostgreSQL)
-- Apply against database: inventory_sync

CREATE TABLE IF NOT EXISTS inventory_items (
    id                  uuid PRIMARY KEY,
    merchant_id         uuid NOT NULL,
    sku                 varchar(128) NOT NULL,
    product_name        varchar(512) NOT NULL,
    available_stock     integer NOT NULL,
    reserved_stock      integer NOT NULL DEFAULT 0,
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_inventory_items_available_nonneg CHECK (available_stock >= 0),
    CONSTRAINT ck_inventory_items_reserved_nonneg CHECK (reserved_stock >= 0),
    CONSTRAINT uq_inventory_items_merchant_sku UNIQUE (merchant_id, sku)
);

CREATE INDEX IF NOT EXISTS ix_inventory_items_merchant_id ON inventory_items (merchant_id);

CREATE TABLE IF NOT EXISTS channel_listings (
    id                  uuid PRIMARY KEY,
    merchant_id         uuid NOT NULL,
    inventory_item_id   uuid NOT NULL REFERENCES inventory_items(id) ON DELETE CASCADE,
    channel             varchar(32) NOT NULL,
    external_product_id varchar(128) NOT NULL,
    external_variant_id varchar(128) NULL,
    is_active           boolean NOT NULL DEFAULT true,
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_channel_listing UNIQUE (merchant_id, channel, external_product_id, external_variant_id)
);

CREATE INDEX IF NOT EXISTS ix_channel_listings_item_channel
    ON channel_listings (inventory_item_id, channel) WHERE is_active = true;

CREATE TABLE IF NOT EXISTS inventory_ledger_logs (
    id                  bigserial PRIMARY KEY,
    inventory_item_id   uuid NOT NULL REFERENCES inventory_items(id),
    merchant_id         uuid NOT NULL,
    sku                 varchar(128) NOT NULL,
    previous_qty        integer NOT NULL,
    delta               integer NOT NULL,
    new_qty             integer NOT NULL,
    source_channel      varchar(32) NOT NULL,
    external_order_id   varchar(128) NULL,
    reason              varchar(64) NOT NULL,
    sync_status         varchar(32) NOT NULL DEFAULT 'Pending',
    created_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_inventory_ledger_balance CHECK (new_qty = previous_qty + delta)
);

CREATE INDEX IF NOT EXISTS ix_inventory_ledger_item_created ON inventory_ledger_logs (inventory_item_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_inventory_ledger_external_order ON inventory_ledger_logs (source_channel, external_order_id);

-- Idempotency gate for inbound webhooks/events
CREATE TABLE IF NOT EXISTS processed_webhook_events (
    id                  bigserial PRIMARY KEY,
    channel             varchar(32) NOT NULL,
    external_event_id   varchar(256) NOT NULL,
    event_type          varchar(128) NOT NULL,
    payload_hash        varchar(128) NULL,
    status              varchar(32) NOT NULL DEFAULT 'Processed',
    received_at         timestamptz NOT NULL DEFAULT now(),
    processed_at        timestamptz NULL,
    CONSTRAINT uq_processed_webhook_event UNIQUE (channel, external_event_id)
);

CREATE INDEX IF NOT EXISTS ix_processed_webhook_received ON processed_webhook_events (received_at DESC);
