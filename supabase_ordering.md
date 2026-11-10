# Supabase migration

Run this PostgreSQL script in the **Supabase SQL Editor**. It is not a SQL Server script and should not be added as a Visual Studio database project file.

```sql
alter table public.groceries
	add column if not exists sort_order integer not null default 0;

with numbered as (
	select id, row_number() over (order by name, id) - 1 as position
	from public.groceries
)
update public.groceries as groceries
set sort_order = numbered.position
from numbered
where groceries.id = numbered.id;
```
