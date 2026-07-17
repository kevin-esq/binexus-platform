CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE TABLE outbox_messages (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        event_name character varying(128) NOT NULL,
        payload_json jsonb NOT NULL,
        schema_version integer NOT NULL,
        occurred_at_utc timestamp with time zone NOT NULL,
        status character varying(32) NOT NULL,
        applicable_handler_keys jsonb,
        attempt_count integer NOT NULL,
        next_attempt_at_utc timestamp with time zone,
        locked_until_utc timestamp with time zone,
        locked_by character varying(128),
        last_error_code character varying(64),
        last_error_message character varying(512),
        correlation_id character varying(128),
        causation_id character varying(128),
        created_at_utc timestamp with time zone NOT NULL,
        initialized_at_utc timestamp with time zone,
        completed_at_utc timestamp with time zone,
        CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE TABLE event_handler_deliveries (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        event_id uuid NOT NULL,
        handler_key character varying(128) NOT NULL,
        status character varying(32) NOT NULL,
        attempt_count integer NOT NULL,
        next_attempt_at_utc timestamp with time zone,
        locked_until_utc timestamp with time zone,
        locked_by character varying(128),
        last_error_code character varying(64),
        last_error_message character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        processed_at_utc timestamp with time zone,
        CONSTRAINT pk_event_handler_deliveries PRIMARY KEY (id),
        CONSTRAINT fk_event_handler_deliveries_outbox_messages_event_id FOREIGN KEY (event_id) REFERENCES outbox_messages (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE INDEX ix_event_handler_deliveries_event_id ON event_handler_deliveries (event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE INDEX ix_event_handler_deliveries_status_locked_until_utc ON event_handler_deliveries (status, locked_until_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE INDEX ix_event_handler_deliveries_status_next_attempt_at_utc ON event_handler_deliveries (status, next_attempt_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE UNIQUE INDEX ix_event_handler_deliveries_tenant_id_event_id_handler_key ON event_handler_deliveries (tenant_id, event_id, handler_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE INDEX ix_outbox_messages_status_locked_until_utc ON outbox_messages (status, locked_until_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    CREATE INDEX ix_outbox_messages_tenant_id_status_next_attempt_at_utc ON outbox_messages (tenant_id, status, next_attempt_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260710104015_Platform_OutboxInbox') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260710104015_Platform_OutboxInbox', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE TABLE tenants (
        id uuid NOT NULL,
        slug character varying(100) NOT NULL,
        name character varying(200) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_tenants PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE TABLE branches (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        CONSTRAINT pk_branches PRIMARY KEY (id),
        CONSTRAINT fk_branches_tenant_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        email character varying(320) NOT NULL,
        normalized_email character varying(320) NOT NULL,
        password_hash character varying(512) NOT NULL,
        role character varying(32) NOT NULL,
        branch_id uuid,
        is_system boolean NOT NULL,
        is_disabled boolean NOT NULL,
        CONSTRAINT pk_users PRIMARY KEY (id),
        CONSTRAINT ck_users_role CHECK (role IN ('SUPER_ADMIN','ADMIN','CASHIER','WAREHOUSE','DRIVER')),
        CONSTRAINT fk_users_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE SET NULL,
        CONSTRAINT fk_users_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE TABLE refresh_tokens (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character(64) NOT NULL,
        family_id uuid NOT NULL,
        parent_token_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        used_at_utc timestamp with time zone,
        revoked_at_utc timestamp with time zone,
        replaced_by_token_id uuid,
        revocation_reason character varying(64),
        CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
        CONSTRAINT fk_refresh_tokens_user_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE UNIQUE INDEX ix_branches_tenant_id_name ON branches (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE INDEX ix_refresh_tokens_family_id_revoked_at_utc ON refresh_tokens (family_id, revoked_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE UNIQUE INDEX ix_tenants_slug ON tenants (slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE INDEX ix_users_branch_id ON users (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    CREATE UNIQUE INDEX ix_users_tenant_id_normalized_email ON users (tenant_id, normalized_email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260711023047_Identity_TenantsUsersBranchesRefresh') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260711023047_Identity_TenantsUsersBranchesRefresh', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE TABLE stock_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        product_id character varying(256) NOT NULL,
        on_hand integer NOT NULL,
        reserved integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_stock_items PRIMARY KEY (id),
        CONSTRAINT ck_stock_items_on_hand_non_negative CHECK (on_hand >= 0),
        CONSTRAINT ck_stock_items_reserved_non_negative CHECK (reserved >= 0),
        CONSTRAINT ck_stock_items_reserved_not_above_on_hand CHECK (reserved <= on_hand)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE TABLE stock_movements (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        product_id character varying(256) NOT NULL,
        quantity integer NOT NULL,
        type character varying(16) NOT NULL,
        operation_key character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_stock_movements PRIMARY KEY (id),
        CONSTRAINT ck_stock_movements_quantity_nonzero CHECK (quantity <> 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE TABLE stock_reservations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        order_id uuid NOT NULL,
        order_line_id uuid NOT NULL,
        product_id character varying(256) NOT NULL,
        quantity integer NOT NULL,
        status character varying(16) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_stock_reservations PRIMARY KEY (id),
        CONSTRAINT ck_stock_reservations_quantity_positive CHECK (quantity > 0),
        CONSTRAINT ck_stock_reservations_status CHECK (status IN ('ACTIVE','RELEASED','FAILED'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE TABLE stock_transfers (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        source_branch_id uuid NOT NULL,
        destination_branch_id uuid NOT NULL,
        product_id character varying(256) NOT NULL,
        quantity integer NOT NULL,
        reason character varying(200),
        status character varying(16) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        received_at_utc timestamp with time zone,
        cancelled_at_utc timestamp with time zone,
        CONSTRAINT pk_stock_transfers PRIMARY KEY (id),
        CONSTRAINT ck_stock_transfers_branches_distinct CHECK (source_branch_id <> destination_branch_id),
        CONSTRAINT ck_stock_transfers_quantity_positive CHECK (quantity > 0),
        CONSTRAINT ck_stock_transfers_status CHECK (status IN ('PENDING','RECEIVED','CANCELLED'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE INDEX ix_stock_items_tenant_id_branch_id ON stock_items (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE UNIQUE INDEX ix_stock_items_tenant_id_branch_id_product_id ON stock_items (tenant_id, branch_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE UNIQUE INDEX ix_stock_movements_tenant_id_operation_key ON stock_movements (tenant_id, operation_key) WHERE operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE UNIQUE INDEX ix_stock_reservations_tenant_id_order_id_order_line_id ON stock_reservations (tenant_id, order_id, order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    CREATE INDEX ix_stock_transfers_tenant_id_status_created_at_utc ON stock_transfers (tenant_id, status, created_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020449_Inventory_Stock') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712020449_Inventory_Stock', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    CREATE INDEX ix_stock_transfers_destination_branch_id ON stock_transfers (destination_branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    CREATE INDEX ix_stock_transfers_source_branch_id ON stock_transfers (source_branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    CREATE INDEX ix_stock_reservations_branch_id ON stock_reservations (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    CREATE INDEX ix_stock_movements_branch_id ON stock_movements (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    CREATE INDEX ix_stock_items_branch_id ON stock_items (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_items ADD CONSTRAINT fk_stock_items_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_items ADD CONSTRAINT fk_stock_items_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_movements ADD CONSTRAINT fk_stock_movements_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_movements ADD CONSTRAINT fk_stock_movements_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_reservations ADD CONSTRAINT fk_stock_reservations_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_reservations ADD CONSTRAINT fk_stock_reservations_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_transfers ADD CONSTRAINT fk_stock_transfers_branches_destination_branch_id FOREIGN KEY (destination_branch_id) REFERENCES branches (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_transfers ADD CONSTRAINT fk_stock_transfers_branches_source_branch_id FOREIGN KEY (source_branch_id) REFERENCES branches (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    ALTER TABLE stock_transfers ADD CONSTRAINT fk_stock_transfers_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712020947_Inventory_StockForeignKeys') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712020947_Inventory_StockForeignKeys', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712024328_Inventory_TransferOperationKey') THEN
    ALTER TABLE stock_transfers ADD operation_key character varying(512);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712024328_Inventory_TransferOperationKey') THEN
    CREATE UNIQUE INDEX ix_stock_transfers_tenant_id_operation_key ON stock_transfers (tenant_id, operation_key) WHERE operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712024328_Inventory_TransferOperationKey') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712024328_Inventory_TransferOperationKey', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE TABLE orders (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        customer_id character varying(256) NOT NULL,
        currency character(3) NOT NULL,
        payment_method character varying(32) NOT NULL,
        total_cents integer NOT NULL,
        state character varying(32) NOT NULL,
        created_by_user_id uuid NOT NULL,
        operation_key character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_orders PRIMARY KEY (id),
        CONSTRAINT ck_orders_currency_iso3 CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_orders_state CHECK (state IN ('DRAFT','APPROVED','PICKING','READY_FOR_DELIVERY_ROUTE','OUT_FOR_DELIVERY','DELIVERY_ATTEMPT_FAILED','DELIVERED','SETTLED','CANCELLED')),
        CONSTRAINT ck_orders_total_cents_non_negative CHECK (total_cents >= 0),
        CONSTRAINT fk_orders_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_orders_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE TABLE order_lines (
        id uuid NOT NULL,
        order_id uuid NOT NULL,
        product_id character varying(256) NOT NULL,
        product_name character varying(512) NOT NULL,
        quantity integer NOT NULL,
        unit_price_cents integer NOT NULL,
        line_total_cents integer NOT NULL,
        CONSTRAINT pk_order_lines PRIMARY KEY (id),
        CONSTRAINT ck_order_lines_quantity_positive CHECK (quantity > 0),
        CONSTRAINT ck_order_lines_total_non_negative CHECK (line_total_cents >= 0),
        CONSTRAINT ck_order_lines_unit_price_non_negative CHECK (unit_price_cents >= 0),
        CONSTRAINT fk_order_lines_orders_order_id FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE TABLE order_transitions (
        id uuid NOT NULL,
        order_id uuid NOT NULL,
        from_state character varying(32),
        to_state character varying(32) NOT NULL,
        reason character varying(512),
        by_user_id uuid NOT NULL,
        operation_key character varying(512),
        occurred_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_order_transitions PRIMARY KEY (id),
        CONSTRAINT fk_order_transitions_orders_order_id FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE INDEX ix_order_lines_order_id_id ON order_lines (order_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE UNIQUE INDEX ix_order_transitions_operation_key ON order_transitions (operation_key) WHERE operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE INDEX ix_order_transitions_order_id_occurred_at_utc_id ON order_transitions (order_id, occurred_at_utc, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE INDEX ix_orders_branch_id ON orders (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE INDEX ix_orders_tenant_id_branch_id_state ON orders (tenant_id, branch_id, state);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE INDEX ix_orders_tenant_id_created_at_utc_id ON orders (tenant_id, created_at_utc, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    CREATE UNIQUE INDEX ix_orders_tenant_id_operation_key ON orders (tenant_id, operation_key) WHERE operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712025159_Orders_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712025159_Orders_Initial', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712040734_Orders_TransitionTenantCorrelation') THEN
    DROP INDEX ix_order_transitions_operation_key;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712040734_Orders_TransitionTenantCorrelation') THEN
    ALTER TABLE order_transitions ADD correlation_id character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712040734_Orders_TransitionTenantCorrelation') THEN
    ALTER TABLE order_transitions ADD tenant_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712040734_Orders_TransitionTenantCorrelation') THEN
    CREATE UNIQUE INDEX ix_order_transitions_tenant_id_operation_key ON order_transitions (tenant_id, operation_key) WHERE operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712040734_Orders_TransitionTenantCorrelation') THEN
    CREATE INDEX ix_order_transitions_tenant_id_order_id_occurred_at_utc ON order_transitions (tenant_id, order_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712040734_Orders_TransitionTenantCorrelation') THEN
    ALTER TABLE order_transitions ADD CONSTRAINT fk_order_transitions_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712040734_Orders_TransitionTenantCorrelation') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712040734_Orders_TransitionTenantCorrelation', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE TABLE picking_tasks (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        order_id uuid NOT NULL,
        status character varying(16) NOT NULL,
        created_from_event_id uuid,
        completed_at_utc timestamp with time zone,
        completed_by_user_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_picking_tasks PRIMARY KEY (id),
        CONSTRAINT ck_picking_tasks_status CHECK (status IN ('PENDING','COMPLETED','CANCELLED')),
        CONSTRAINT fk_picking_tasks_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_picking_tasks_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE TABLE picking_lines (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        picking_task_id uuid NOT NULL,
        order_line_id uuid NOT NULL,
        product_id character varying(256) NOT NULL,
        quantity integer NOT NULL,
        picked_quantity integer NOT NULL,
        CONSTRAINT pk_picking_lines PRIMARY KEY (id),
        CONSTRAINT ck_picking_lines_picked_non_negative CHECK (picked_quantity >= 0),
        CONSTRAINT ck_picking_lines_picked_not_above_quantity CHECK (picked_quantity <= quantity),
        CONSTRAINT ck_picking_lines_quantity_positive CHECK (quantity > 0),
        CONSTRAINT fk_picking_lines_picking_task_picking_task_id FOREIGN KEY (picking_task_id) REFERENCES picking_tasks (id) ON DELETE CASCADE,
        CONSTRAINT fk_picking_lines_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE INDEX ix_picking_lines_picking_task_id ON picking_lines (picking_task_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE UNIQUE INDEX ix_picking_lines_tenant_id_picking_task_id_order_line_id ON picking_lines (tenant_id, picking_task_id, order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE INDEX ix_picking_tasks_branch_id ON picking_tasks (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE INDEX ix_picking_tasks_tenant_id_branch_id ON picking_tasks (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE UNIQUE INDEX ix_picking_tasks_tenant_id_order_id ON picking_tasks (tenant_id, order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    CREATE INDEX ix_picking_tasks_tenant_id_status ON picking_tasks (tenant_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712042904_Warehouse_Picking') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712042904_Warehouse_Picking', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    UPDATE picking_tasks
    SET created_from_event_id = id
    WHERE created_from_event_id IS NULL
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    ALTER TABLE picking_tasks ALTER COLUMN created_from_event_id SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    ALTER TABLE picking_tasks ADD completion_operation_key character varying(512);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    ALTER TABLE order_transitions ALTER COLUMN by_user_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    ALTER TABLE order_transitions ADD causation_id character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    ALTER TABLE order_transitions ADD source character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    CREATE UNIQUE INDEX ix_picking_tasks_tenant_id_completion_operation_key ON picking_tasks (tenant_id, completion_operation_key) WHERE completion_operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    CREATE UNIQUE INDEX ix_picking_tasks_tenant_id_created_from_event_id ON picking_tasks (tenant_id, created_from_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712045916_Warehouse_CloseAdjustments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712045916_Warehouse_CloseAdjustments', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE TABLE delivery_route_candidates (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        order_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        status character varying(16) NOT NULL,
        created_from_event_id uuid NOT NULL,
        delivery_route_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_delivery_route_candidates PRIMARY KEY (id),
        CONSTRAINT ck_delivery_route_candidates_status CHECK (status IN ('READY','ASSIGNED','CANCELLED')),
        CONSTRAINT fk_delivery_route_candidates_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_delivery_route_candidates_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE TABLE delivery_route_liquidations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        delivery_route_id uuid NOT NULL,
        expected_cents integer NOT NULL,
        declared_cents integer NOT NULL,
        currency character(3) NOT NULL,
        discrepancy_reason character varying(512),
        notes character varying(1024),
        liquidated_by_user_id uuid,
        operation_key character varying(512) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_delivery_route_liquidations PRIMARY KEY (id),
        CONSTRAINT ck_delivery_route_liquidations_currency_iso3 CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_delivery_route_liquidations_declared_non_negative CHECK (declared_cents >= 0),
        CONSTRAINT ck_delivery_route_liquidations_expected_non_negative CHECK (expected_cents >= 0),
        CONSTRAINT fk_delivery_route_liquidations_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE TABLE delivery_routes (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        status character varying(16) NOT NULL,
        driver_user_id uuid,
        planned_date date,
        dispatched_at_utc timestamp with time zone,
        completed_at_utc timestamp with time zone,
        creation_operation_key character varying(512) NOT NULL,
        assign_operation_key character varying(512),
        dispatch_operation_key character varying(512),
        completion_operation_key character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_delivery_routes PRIMARY KEY (id),
        CONSTRAINT ck_delivery_routes_status CHECK (status IN ('PLANNED','DISPATCHED','COMPLETED','CANCELLED')),
        CONSTRAINT fk_delivery_routes_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_delivery_routes_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE TABLE delivery_route_liquidation_lines (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        delivery_route_liquidation_id uuid NOT NULL,
        delivery_route_stop_id uuid NOT NULL,
        order_id uuid NOT NULL,
        expected_cents integer NOT NULL,
        declared_cents integer NOT NULL,
        CONSTRAINT pk_delivery_route_liquidation_lines PRIMARY KEY (id),
        CONSTRAINT ck_delivery_route_liquidation_lines_declared_non_negative CHECK (declared_cents >= 0),
        CONSTRAINT ck_delivery_route_liquidation_lines_expected_non_negative CHECK (expected_cents >= 0),
        CONSTRAINT fk_delivery_route_liquidation_lines_delivery_route_liquidation FOREIGN KEY (delivery_route_liquidation_id) REFERENCES delivery_route_liquidations (id) ON DELETE CASCADE,
        CONSTRAINT fk_delivery_route_liquidation_lines_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE TABLE delivery_route_stops (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        delivery_route_id uuid NOT NULL,
        order_id uuid NOT NULL,
        sequence integer NOT NULL,
        status character varying(16) NOT NULL,
        failure_reason character varying(32),
        failure_notes character varying(512),
        delivered_at_utc timestamp with time zone,
        failed_at_utc timestamp with time zone,
        completion_operation_key character varying(512),
        failure_operation_key character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_delivery_route_stops PRIMARY KEY (id),
        CONSTRAINT ck_delivery_route_stops_sequence_positive CHECK (sequence > 0),
        CONSTRAINT ck_delivery_route_stops_status CHECK (status IN ('PLANNED','DELIVERED','FAILED','SKIPPED')),
        CONSTRAINT fk_delivery_route_stops_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_delivery_route_stops_delivery_routes_delivery_route_id FOREIGN KEY (delivery_route_id) REFERENCES delivery_routes (id) ON DELETE CASCADE,
        CONSTRAINT fk_delivery_route_stops_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE TABLE delivery_proofs (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        delivery_route_stop_id uuid NOT NULL,
        photo_object_key character varying(512),
        signature_object_key character varying(512),
        recipient character varying(256),
        notes character varying(1024),
        latitude numeric(9,6),
        longitude numeric(9,6),
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_delivery_proofs PRIMARY KEY (id),
        CONSTRAINT fk_delivery_proofs_delivery_route_stop_delivery_route_stop_id FOREIGN KEY (delivery_route_stop_id) REFERENCES delivery_route_stops (id) ON DELETE CASCADE,
        CONSTRAINT fk_delivery_proofs_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_proofs_delivery_route_stop_id ON delivery_proofs (delivery_route_stop_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_proofs_tenant_id_delivery_route_stop_id ON delivery_proofs (tenant_id, delivery_route_stop_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_route_candidates_branch_id ON delivery_route_candidates (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_route_candidates_tenant_id_branch_id_status ON delivery_route_candidates (tenant_id, branch_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_candidates_tenant_id_created_from_event_id ON delivery_route_candidates (tenant_id, created_from_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_candidates_tenant_id_order_id ON delivery_route_candidates (tenant_id, order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_route_liquidation_lines_delivery_route_liquidation ON delivery_route_liquidation_lines (delivery_route_liquidation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_liquidation_lines_tenant_id_delivery_route_l ON delivery_route_liquidation_lines (tenant_id, delivery_route_liquidation_id, delivery_route_stop_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_liquidations_tenant_id_delivery_route_id ON delivery_route_liquidations (tenant_id, delivery_route_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_liquidations_tenant_id_operation_key ON delivery_route_liquidations (tenant_id, operation_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_route_stops_branch_id ON delivery_route_stops (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_route_stops_delivery_route_id ON delivery_route_stops (delivery_route_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_stops_tenant_id_completion_operation_key ON delivery_route_stops (tenant_id, completion_operation_key) WHERE completion_operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_stops_tenant_id_delivery_route_id_sequence ON delivery_route_stops (tenant_id, delivery_route_id, sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_route_stops_tenant_id_failure_operation_key ON delivery_route_stops (tenant_id, failure_operation_key) WHERE failure_operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_route_stops_tenant_id_order_id ON delivery_route_stops (tenant_id, order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_routes_branch_id ON delivery_routes (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_routes_tenant_id_branch_id_status ON delivery_routes (tenant_id, branch_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE INDEX ix_delivery_routes_tenant_id_created_at_utc_id ON delivery_routes (tenant_id, created_at_utc, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    CREATE UNIQUE INDEX ix_delivery_routes_tenant_id_creation_operation_key ON delivery_routes (tenant_id, creation_operation_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712052515_Logistics_DeliveryRoutes') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712052515_Logistics_DeliveryRoutes', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    ALTER TABLE delivery_route_liquidation_lines ADD included boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    ALTER TABLE delivery_route_liquidation_lines ADD payment_method character varying(32) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    CREATE TABLE delivery_proof_upload_intents (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        operation_key character varying(512) NOT NULL,
        stop_id uuid NOT NULL,
        kind character varying(32) NOT NULL,
        content_type character varying(128) NOT NULL,
        size_bytes bigint NOT NULL,
        object_key character varying(512) NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_delivery_proof_upload_intents PRIMARY KEY (id),
        CONSTRAINT fk_delivery_proof_upload_intents_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    CREATE TABLE tenant_features (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        key character varying(64) NOT NULL,
        enabled boolean NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_tenant_features PRIMARY KEY (id),
        CONSTRAINT fk_tenant_features_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    CREATE UNIQUE INDEX ix_delivery_route_stops_tenant_id_delivery_route_id_order_id ON delivery_route_stops (tenant_id, delivery_route_id, order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    CREATE UNIQUE INDEX ix_delivery_proofs_photo_object_key ON delivery_proofs (photo_object_key) WHERE photo_object_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    CREATE UNIQUE INDEX ix_delivery_proofs_signature_object_key ON delivery_proofs (signature_object_key) WHERE signature_object_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    CREATE UNIQUE INDEX ix_delivery_proof_upload_intents_tenant_id_operation_key ON delivery_proof_upload_intents (tenant_id, operation_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    CREATE UNIQUE INDEX ix_tenant_features_tenant_id_key ON tenant_features (tenant_id, key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712062139_Logistics_CloseAdjustments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712062139_Logistics_CloseAdjustments', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE TABLE sales_sessions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        terminal_id character varying(50) NOT NULL,
        status character varying(16) NOT NULL,
        opening_float_cents integer NOT NULL,
        currency character varying(3) NOT NULL,
        opened_by_user_id uuid NOT NULL,
        opened_at_utc timestamp with time zone NOT NULL,
        closed_by_user_id uuid,
        closed_at_utc timestamp with time zone,
        expected_closing_cents integer,
        declared_closing_cents integer,
        discrepancy_cents integer,
        discrepancy_reason character varying(512),
        close_notes character varying(2000),
        open_operation_key character varying(512) NOT NULL,
        close_operation_key character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_sales_sessions PRIMARY KEY (id),
        CONSTRAINT ck_sales_sessions_currency_iso3 CHECK (char_length(currency) = 3),
        CONSTRAINT ck_sales_sessions_opening_float_non_negative CHECK (opening_float_cents >= 0),
        CONSTRAINT ck_sales_sessions_status CHECK (status IN ('OPEN','CLOSED')),
        CONSTRAINT fk_sales_sessions_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_sessions_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE TABLE sales (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        session_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        terminal_id character varying(50) NOT NULL,
        customer_label character varying(64) NOT NULL,
        status character varying(16) NOT NULL,
        total_cents integer NOT NULL,
        currency character varying(3) NOT NULL,
        cashier_user_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        operation_key character varying(512),
        CONSTRAINT pk_sales PRIMARY KEY (id),
        CONSTRAINT ck_sales_currency_iso3 CHECK (char_length(currency) = 3),
        CONSTRAINT ck_sales_status CHECK (status IN ('COMPLETED')),
        CONSTRAINT ck_sales_total_non_negative CHECK (total_cents >= 0),
        CONSTRAINT fk_sales_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_sales_session_session_id FOREIGN KEY (session_id) REFERENCES sales_sessions (id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE TABLE payment_captures (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sale_id uuid NOT NULL,
        session_id uuid NOT NULL,
        method character varying(16) NOT NULL,
        amount_cents integer NOT NULL,
        currency character varying(3) NOT NULL,
        captured_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_payment_captures PRIMARY KEY (id),
        CONSTRAINT ck_payment_captures_amount_positive CHECK (amount_cents > 0),
        CONSTRAINT ck_payment_captures_currency_iso3 CHECK (char_length(currency) = 3),
        CONSTRAINT ck_payment_captures_method CHECK (method IN ('CASH','CARD','TRANSFER')),
        CONSTRAINT fk_payment_captures_sale_sale_id FOREIGN KEY (sale_id) REFERENCES sales (id) ON DELETE CASCADE,
        CONSTRAINT fk_payment_captures_sales_session_session_id FOREIGN KEY (session_id) REFERENCES sales_sessions (id) ON DELETE RESTRICT,
        CONSTRAINT fk_payment_captures_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE TABLE sale_lines (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sale_id uuid NOT NULL,
        product_id character varying(256) NOT NULL,
        product_name character varying(256) NOT NULL,
        quantity integer NOT NULL,
        unit_price_cents integer NOT NULL,
        line_total_cents integer NOT NULL,
        CONSTRAINT pk_sale_lines PRIMARY KEY (id),
        CONSTRAINT ck_sale_lines_line_total_non_negative CHECK (line_total_cents >= 0),
        CONSTRAINT ck_sale_lines_quantity_positive CHECK (quantity > 0),
        CONSTRAINT ck_sale_lines_unit_price_non_negative CHECK (unit_price_cents >= 0),
        CONSTRAINT fk_sale_lines_sales_sale_id FOREIGN KEY (sale_id) REFERENCES sales (id) ON DELETE CASCADE,
        CONSTRAINT fk_sale_lines_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_payment_captures_sale_id ON payment_captures (sale_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_payment_captures_session_id ON payment_captures (session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_payment_captures_tenant_id_sale_id ON payment_captures (tenant_id, sale_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_payment_captures_tenant_id_session_id_method ON payment_captures (tenant_id, session_id, method);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_sale_lines_sale_id ON sale_lines (sale_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_sale_lines_tenant_id_sale_id ON sale_lines (tenant_id, sale_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_sales_branch_id ON sales (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_sales_session_id ON sales (session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE UNIQUE INDEX ix_sales_tenant_id_operation_key ON sales (tenant_id, operation_key) WHERE operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_sales_tenant_id_session_id ON sales (tenant_id, session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_sales_sessions_branch_id ON sales_sessions (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE UNIQUE INDEX ix_sales_sessions_open_terminal_unique ON sales_sessions (tenant_id, branch_id, terminal_id) WHERE status = 'OPEN';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE INDEX ix_sales_sessions_tenant_id_branch_id_terminal_id_status ON sales_sessions (tenant_id, branch_id, terminal_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE UNIQUE INDEX ix_sales_sessions_tenant_id_close_operation_key ON sales_sessions (tenant_id, close_operation_key) WHERE close_operation_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    CREATE UNIQUE INDEX ix_sales_sessions_tenant_id_open_operation_key ON sales_sessions (tenant_id, open_operation_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712064852_Sales_SessionsAndSales') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712064852_Sales_SessionsAndSales', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    ALTER TABLE payment_captures DROP CONSTRAINT fk_payment_captures_sale_sale_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    ALTER TABLE payment_captures DROP CONSTRAINT fk_payment_captures_sales_session_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    ALTER TABLE sales DROP CONSTRAINT fk_sales_sales_session_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    DROP INDEX ix_sales_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    DROP INDEX ix_payment_captures_sale_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    DROP INDEX ix_payment_captures_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    ALTER TABLE sales_sessions ADD CONSTRAINT ak_sales_sessions_tenant_id UNIQUE (tenant_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    ALTER TABLE sales ADD CONSTRAINT ak_sales_tenant_id_session UNIQUE (tenant_id, id, session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    CREATE INDEX ix_payment_captures_tenant_id_sale_id_session_id ON payment_captures (tenant_id, sale_id, session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    ALTER TABLE payment_captures ADD CONSTRAINT fk_payment_captures_sales_tenant_id_sale_id_session_id FOREIGN KEY (tenant_id, sale_id, session_id) REFERENCES sales (tenant_id, id, session_id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    ALTER TABLE sales ADD CONSTRAINT fk_sales_sales_session_tenant_id_session_id FOREIGN KEY (tenant_id, session_id) REFERENCES sales_sessions (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260712072341_Sales_ClosingAdjustments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260712072341_Sales_ClosingAdjustments', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715110030_Platform_BranchInstance') THEN
    CREATE TABLE branch_instances (
        id uuid NOT NULL,
        singleton_key character varying(16) NOT NULL,
        status character varying(64) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_branch_instances PRIMARY KEY (id),
        CONSTRAINT ck_branch_instances_singleton_key_local CHECK (singleton_key = 'local'),
        CONSTRAINT ck_branch_instances_status_ready_for_activation CHECK (status = 'ReadyForActivation')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715110030_Platform_BranchInstance') THEN
    CREATE UNIQUE INDEX ix_branch_instances_singleton_key ON branch_instances (singleton_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715110030_Platform_BranchInstance') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715110030_Platform_BranchInstance', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    ALTER TABLE branch_instances DROP CONSTRAINT ck_branch_instances_status_ready_for_activation;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    ALTER TABLE branch_instances ADD activated_at_utc timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    ALTER TABLE branch_instances ADD branch_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    ALTER TABLE branch_instances ADD cloud_activation_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    ALTER TABLE branch_instances ADD tenant_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    CREATE TABLE branch_activation_challenges (
        id uuid NOT NULL,
        branch_instance_id uuid NOT NULL,
        public_key_fingerprint character varying(64) NOT NULL,
        installation_token_hash character varying(64) NOT NULL,
        nonce character varying(128) NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        consumed_at_utc timestamp with time zone,
        CONSTRAINT pk_branch_activation_challenges PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    CREATE TABLE branch_activations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        code_hash character varying(64) NOT NULL,
        status character varying(16) NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        reserved_until_utc timestamp with time zone,
        adopted_branch_instance_id uuid,
        public_key_fingerprint character varying(64),
        installation_token_hash character varying(64),
        activation_receipt_hash character varying(64),
        failed_attempt_count integer NOT NULL,
        locked_until_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid NOT NULL,
        reserved_at_utc timestamp with time zone,
        consumed_at_utc timestamp with time zone,
        CONSTRAINT pk_branch_activations PRIMARY KEY (id),
        CONSTRAINT ck_branch_activations_status CHECK (status IN ('Open', 'Reserved', 'Consumed', 'Expired'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    CREATE TABLE cloud_branch_instances (
        branch_instance_id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        status character varying(16) NOT NULL,
        installation_token_hash character varying(64) NOT NULL,
        public_key text NOT NULL,
        public_key_fingerprint character varying(64) NOT NULL,
        activation_id uuid NOT NULL,
        activating_until_utc timestamp with time zone,
        activated_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_cloud_branch_instances PRIMARY KEY (branch_instance_id),
        CONSTRAINT ck_cloud_branch_instances_status CHECK (status IN ('Activating', 'Active'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    ALTER TABLE branch_instances ADD CONSTRAINT ck_branch_instances_status CHECK (status IN ('ReadyForActivation', 'Active'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    CREATE INDEX ix_branch_activation_challenges_branch_instance_id_expires_at_ ON branch_activation_challenges (branch_instance_id, expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    CREATE UNIQUE INDEX ix_branch_activations_code_hash ON branch_activations (code_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    CREATE UNIQUE INDEX ix_branch_activations_tenant_id_branch_id ON branch_activations (tenant_id, branch_id) WHERE status IN ('Open', 'Reserved');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    CREATE UNIQUE INDEX ix_cloud_branch_instances_tenant_id_branch_id ON cloud_branch_instances (tenant_id, branch_id) WHERE status IN ('Activating', 'Active');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715130453_Platform_BranchActivation') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715130453_Platform_BranchActivation', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE TABLE branch_devices (
        id uuid NOT NULL,
        branch_instance_id uuid NOT NULL,
        public_key character varying(512) NOT NULL,
        public_key_fingerprint character varying(64) NOT NULL,
        credential_hash character varying(64) NOT NULL,
        status character varying(24) NOT NULL,
        pairing_request_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        paired_at_utc timestamp with time zone,
        revoked_at_utc timestamp with time zone,
        revoked_by_user_id uuid,
        CONSTRAINT pk_branch_devices PRIMARY KEY (id),
        CONSTRAINT ck_branch_devices_status CHECK (status IN ('PendingConfirmation', 'Active', 'Revoked'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE TABLE branch_terminals (
        id uuid NOT NULL,
        branch_instance_id uuid NOT NULL,
        device_id uuid NOT NULL,
        name character varying(50) NOT NULL,
        normalized_name character varying(50) NOT NULL,
        status character varying(24) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        activated_at_utc timestamp with time zone,
        CONSTRAINT pk_branch_terminals PRIMARY KEY (id),
        CONSTRAINT ck_branch_terminals_status CHECK (status IN ('PendingConfirmation', 'Active', 'Disabled'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE TABLE device_pairing_challenges (
        id uuid NOT NULL,
        phase character varying(16) NOT NULL,
        branch_instance_id uuid NOT NULL,
        pairing_session_id uuid,
        pairing_request_id uuid,
        device_id uuid NOT NULL,
        terminal_id uuid,
        public_key_fingerprint character varying(64) NOT NULL,
        credential_hash character varying(64) NOT NULL,
        pairing_receipt_hash character varying(64),
        nonce character varying(128) NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        consumed_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_device_pairing_challenges PRIMARY KEY (id),
        CONSTRAINT ck_device_pairing_challenges_phase CHECK (phase IN ('Exchange', 'Confirmation')),
        CONSTRAINT ck_device_pairing_challenges_phase_targets CHECK ((phase = 'Exchange' AND pairing_session_id IS NOT NULL) OR (phase = 'Confirmation' AND pairing_request_id IS NOT NULL AND pairing_receipt_hash IS NOT NULL))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE TABLE device_pairing_requests (
        id uuid NOT NULL,
        pairing_session_id uuid NOT NULL,
        branch_instance_id uuid NOT NULL,
        device_id uuid NOT NULL,
        public_key character varying(512) NOT NULL,
        public_key_fingerprint character varying(64) NOT NULL,
        credential_hash character varying(64) NOT NULL,
        requested_terminal_name character varying(50) NOT NULL,
        requested_terminal_name_normalized character varying(50) NOT NULL,
        status character varying(16) NOT NULL,
        status_token_hash character varying(64) NOT NULL,
        status_token_expires_at_utc timestamp with time zone NOT NULL,
        requested_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        terminal_id uuid,
        pairing_receipt_hash character varying(64),
        approved_at_utc timestamp with time zone,
        approved_by_user_id uuid,
        rejected_at_utc timestamp with time zone,
        rejected_by_user_id uuid,
        completed_at_utc timestamp with time zone,
        CONSTRAINT pk_device_pairing_requests PRIMARY KEY (id),
        CONSTRAINT ck_device_pairing_requests_status CHECK (status IN ('PendingApproval', 'Approved', 'Rejected', 'Expired', 'Completed'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE TABLE device_pairing_sessions (
        id uuid NOT NULL,
        branch_instance_id uuid NOT NULL,
        code_hash character varying(64) NOT NULL,
        status character varying(16) NOT NULL,
        created_by_user_id uuid NOT NULL,
        failed_attempt_count integer NOT NULL,
        locked_until_utc timestamp with time zone,
        expires_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        consumed_at_utc timestamp with time zone,
        CONSTRAINT pk_device_pairing_sessions PRIMARY KEY (id),
        CONSTRAINT ck_device_pairing_sessions_status CHECK (status IN ('Open', 'Consumed', 'Expired'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE UNIQUE INDEX ix_branch_devices_branch_instance_id_credential_hash ON branch_devices (branch_instance_id, credential_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE UNIQUE INDEX ix_branch_devices_branch_instance_id_public_key_fingerprint ON branch_devices (branch_instance_id, public_key_fingerprint);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE UNIQUE INDEX ix_branch_devices_pairing_request_id ON branch_devices (pairing_request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE UNIQUE INDEX ix_branch_terminals_branch_instance_id_normalized_name ON branch_terminals (branch_instance_id, normalized_name) WHERE status IN ('PendingConfirmation', 'Active');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE UNIQUE INDEX ix_branch_terminals_device_id ON branch_terminals (device_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE INDEX ix_device_pairing_challenges_branch_instance_id_expires_at_utc ON device_pairing_challenges (branch_instance_id, expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE INDEX ix_device_pairing_challenges_pairing_request_id ON device_pairing_challenges (pairing_request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE UNIQUE INDEX ix_device_pairing_requests_pairing_session_id_device_id ON device_pairing_requests (pairing_session_id, device_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE INDEX ix_device_pairing_sessions_branch_instance_id_status ON device_pairing_sessions (branch_instance_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    CREATE UNIQUE INDEX ix_device_pairing_sessions_code_hash ON device_pairing_sessions (code_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260717072639_Platform_BranchDevicePairing') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260717072639_Platform_BranchDevicePairing', '10.0.9');
    END IF;
END $EF$;
COMMIT;

