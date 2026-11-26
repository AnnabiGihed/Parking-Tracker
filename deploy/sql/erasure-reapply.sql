-- Applies an exported erasure log to a restored database (architecture 6.1, ADR-0015).
--
-- pt-erasure-reapply runs this file inside one transaction, after it created and filled the temporary table
--   erasure_import (subject_hash text, erased_on date)
-- from the exported log, and ends the transaction with ROLLBACK (dry run) or COMMIT. The file holds plain SQL only, so a test can run
-- it the same way. Each statement prints one row, so the dry run shows what a commit would change.

-- 1. The accounts that were erased after the dump: their sessions and their encrypted profile go with them (ON DELETE CASCADE). An account created after the
-- day of the erasure is a new sign-up of the same person and stays; one created on that day cannot be told apart from the old one.
WITH erased AS (
	DELETE FROM user_accounts account
	USING erasure_import imported
	WHERE account."SubjectHash" = imported.subject_hash AND account."CreatedOn" <= imported.erased_on
	RETURNING 1
)
SELECT count(*) AS accounts_deleted FROM erased;

-- 2. The log itself, so the restored database knows about these erasures for the next restore; the later day wins.
WITH merged AS (
	INSERT INTO erasure_log ("SubjectHash", "ErasedOn")
	SELECT subject_hash, erased_on FROM erasure_import
	ON CONFLICT ("SubjectHash") DO UPDATE SET "ErasedOn" = GREATEST(erasure_log."ErasedOn", EXCLUDED."ErasedOn")
	RETURNING 1
)
SELECT count(*) AS log_rows_merged FROM merged;

-- 3. Rows older than six months: no backup that old is restored. The cleanup job of S6 purges the live log in the same way.
WITH purged AS (
	DELETE FROM erasure_log
	WHERE "ErasedOn" < (current_date - interval '6 months')::date
	RETURNING 1
)
SELECT count(*) AS log_rows_purged FROM purged;
