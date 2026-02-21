# SQL Tracking Schema

Use the session SQL database to track collected PRs and their lineage.

## servicing_prs table

```sql
CREATE TABLE servicing_prs (
    pr_number INTEGER PRIMARY KEY,
    version TEXT,
    milestone TEXT,
    title TEXT,
    area TEXT,
    author TEXT,
    component TEXT,
    has_product_source INTEGER DEFAULT 1,
    classification TEXT DEFAULT 'product',
    in_scope INTEGER DEFAULT 1,
    main_pr INTEGER,
    direct_to_release INTEGER DEFAULT 0,
    servicing_state TEXT DEFAULT 'none',
    lead TEXT
);
```

## fix_groups table

```sql
CREATE TABLE fix_groups (
    group_id INTEGER PRIMARY KEY AUTOINCREMENT,
    main_pr INTEGER,
    fix_description TEXT,
    component TEXT,
    area TEXT,
    lead TEXT,
    issue_unknown INTEGER DEFAULT 0,
    direct_to_release INTEGER DEFAULT 0,
    repro_found INTEGER DEFAULT 0,
    repro_url TEXT,
    repro_notes TEXT
);
```

## fix_group_issues table

```sql
CREATE TABLE fix_group_issues (
    group_id INTEGER,
    issue_number INTEGER,
    issue_title TEXT,
    PRIMARY KEY (group_id, issue_number)
);
```

## fix_group_servicing_prs table

```sql
CREATE TABLE fix_group_servicing_prs (
    group_id INTEGER,
    pr_number INTEGER,
    version TEXT,
    PRIMARY KEY (group_id, pr_number)
);
```

## Key Queries

Final curated list with lineage:

```sql
SELECT
    fg.group_id,
    fg.fix_description,
    fg.lead,
    fg.component,
    fg.area,
    fg.main_pr,
    fg.issue_unknown,
    fg.direct_to_release,
    GROUP_CONCAT(DISTINCT fgi.issue_number) as issues,
    GROUP_CONCAT(DISTINCT fgsp.pr_number || '(' || fgsp.version || ')') as servicing_prs
FROM fix_groups fg
LEFT JOIN fix_group_issues fgi ON fg.group_id = fgi.group_id
JOIN fix_group_servicing_prs fgsp ON fg.group_id = fgsp.group_id
JOIN servicing_prs sp ON fgsp.pr_number = sp.pr_number
WHERE sp.in_scope = 1 AND sp.has_product_source = 1
GROUP BY fg.group_id
ORDER BY fg.component, fg.area;
```

Track curation state with the `in_scope` column. When the user removes a PR, set `in_scope = 0`. When they add one back, set `in_scope = 1`.
