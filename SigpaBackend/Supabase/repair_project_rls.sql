alter table public.proyectos enable row level security;

revoke all on public.proyectos from public, anon;
grant usage on schema public to authenticated;
grant select, insert, update, delete on public.proyectos to authenticated;

do $$
declare
  policy_record record;
begin
  for policy_record in
    select policyname
    from pg_policies
    where schemaname = 'public'
      and tablename = 'proyectos'
  loop
    execute format('drop policy %I on public.proyectos', policy_record.policyname);
  end loop;
end;
$$;

create policy "Authenticated users can read their projects and staff can read all"
on public.proyectos
for select
to authenticated
using (
  creador_id = (select auth.uid())
  or exists (
    select 1
    from public.usuarios profile
    where profile.id = (select auth.uid())
      and profile.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
);

create policy "Authenticated users can create their own projects"
on public.proyectos
for insert
to authenticated
with check (creador_id = (select auth.uid()));

create policy "Owners and staff can update projects"
on public.proyectos
for update
to authenticated
using (
  creador_id = (select auth.uid())
  or exists (
    select 1
    from public.usuarios profile
    where profile.id = (select auth.uid())
      and profile.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
)
with check (
  creador_id = (select auth.uid())
  or exists (
    select 1
    from public.usuarios profile
    where profile.id = (select auth.uid())
      and profile.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
);

create policy "Owners and staff can delete projects"
on public.proyectos
for delete
to authenticated
using (
  creador_id = (select auth.uid())
  or exists (
    select 1
    from public.usuarios profile
    where profile.id = (select auth.uid())
      and profile.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
);

notify pgrst, 'reload schema';
