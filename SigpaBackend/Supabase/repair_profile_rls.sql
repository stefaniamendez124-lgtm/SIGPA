alter table public.usuarios enable row level security;

revoke all on public.usuarios from public, anon;
grant usage on schema public to authenticated;
grant select on public.usuarios to authenticated;

drop policy if exists "Users can read their own profile" on public.usuarios;
create policy "Users can read their own profile"
on public.usuarios
for select
to authenticated
using (id = (select auth.uid()));

notify pgrst, 'reload schema';
