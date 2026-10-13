create table if not exists public.usuarios (
  id uuid primary key references auth.users(id) on delete cascade,
  nombre_completo text not null default '',
  correo_institucional text not null unique,
  rol text not null default 'Estudiante'
    check (rol in ('Estudiante', 'Profesor', 'Coordinador', 'Administrador')),
  fecha_creacion timestamptz not null default now()
);

create table if not exists public.proyectos (
  id uuid primary key default gen_random_uuid(),
  nombre text not null,
  descripcion text not null default '',
  curso text not null default 'Ingeniería de requisitos',
  estado text not null default 'Pendiente'
    check (estado in ('Pendiente', 'En revisión', 'Aprobado', 'Rechazado', 'Entregado', 'En progreso')),
  progreso integer not null default 15 check (progreso between 0 and 100),
  fecha_inicio timestamptz not null default now(),
  fecha_fin timestamptz not null default (now() + interval '14 days'),
  fecha_entrega date,
  creador_id uuid not null references public.usuarios(id) on delete cascade,
  comentarios_evaluacion text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

alter table public.proyectos
  add column if not exists curso text not null default 'Ingeniería de requisitos',
  add column if not exists progreso integer not null default 15,
  add column if not exists fecha_inicio timestamptz not null default now(),
  add column if not exists fecha_fin timestamptz not null default (now() + interval '14 days'),
  add column if not exists fecha_entrega date,
  add column if not exists created_at timestamptz not null default now(),
  add column if not exists updated_at timestamptz not null default now();

update public.proyectos
set fecha_entrega = fecha_fin::date
where fecha_entrega is null;

create table if not exists public.proyecto_integrantes (
  id uuid primary key default gen_random_uuid(),
  proyecto_id uuid not null references public.proyectos(id) on delete cascade,
  usuario_id uuid not null references public.usuarios(id) on delete cascade,
  created_at timestamptz not null default now(),
  unique (proyecto_id, usuario_id)
);

create table if not exists public.entregables (
  id uuid primary key default gen_random_uuid(),
  proyecto_id uuid not null references public.proyectos(id) on delete cascade,
  titulo text not null,
  descripcion text not null default '',
  porcentaje_nota numeric(5,2) not null default 0,
  fecha_limite date,
  archivo_url text,
  fecha_entrega timestamptz,
  estado text not null default 'Pendiente',
  created_at timestamptz not null default now()
);

create index if not exists proyectos_creador_idx on public.proyectos(creador_id);
create index if not exists proyecto_integrantes_proyecto_idx on public.proyecto_integrantes(proyecto_id);
create index if not exists entregables_proyecto_idx on public.entregables(proyecto_id);

create or replace function public.crear_perfil_usuario()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  insert into public.usuarios (id, nombre_completo, correo_institucional, rol)
  values (
    new.id,
    coalesce(new.raw_user_meta_data ->> 'nombre_completo', ''),
    new.email,
    'Estudiante'
  )
  on conflict (id) do update
    set correo_institucional = excluded.correo_institucional;
  return new;
end;
$$;

drop trigger if exists al_crear_usuario_sigpa on auth.users;
create trigger al_crear_usuario_sigpa
after insert on auth.users
for each row execute procedure public.crear_perfil_usuario();

alter table public.usuarios enable row level security;
alter table public.proyectos enable row level security;
alter table public.proyecto_integrantes enable row level security;
alter table public.entregables enable row level security;

do $$
declare
  policy_record record;
begin
  for policy_record in
    select schemaname, tablename, policyname
    from pg_policies
    where schemaname = 'public'
      and tablename in ('usuarios', 'proyectos', 'proyecto_integrantes', 'entregables')
  loop
    execute format(
      'drop policy %I on %I.%I',
      policy_record.policyname,
      policy_record.schemaname,
      policy_record.tablename
    );
  end loop;
end;
$$;

revoke all on public.usuarios, public.proyectos, public.proyecto_integrantes, public.entregables
from public, anon;
grant usage on schema public to authenticated;
grant select on public.usuarios to authenticated;
grant select, insert, update, delete on public.proyectos to authenticated;
grant select, insert, delete on public.proyecto_integrantes to authenticated;
grant select, insert, update, delete on public.entregables to authenticated;

drop policy if exists "Users can read their own profile" on public.usuarios;
create policy "Users can read their own profile"
on public.usuarios for select to authenticated
using (id = (select auth.uid()));

drop policy if exists "Students and staff can read projects" on public.proyectos;
create policy "Students and staff can read projects"
on public.proyectos for select to authenticated
using (
  creador_id = (select auth.uid())
  or exists (
    select 1 from public.usuarios u
    where u.id = (select auth.uid())
      and u.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
);

drop policy if exists "Students can create their own projects" on public.proyectos;
create policy "Students can create their own projects"
on public.proyectos for insert to authenticated
with check (
  creador_id = (select auth.uid())
  and exists (
    select 1 from public.usuarios u
    where u.id = (select auth.uid()) and u.rol = 'Estudiante'
  )
);

drop policy if exists "Project owners and staff can update projects" on public.proyectos;
create policy "Project owners and staff can update projects"
on public.proyectos for update to authenticated
using (
  creador_id = (select auth.uid())
  or exists (
    select 1 from public.usuarios u
    where u.id = (select auth.uid())
      and u.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
)
with check (
  creador_id = (select auth.uid())
  or exists (
    select 1 from public.usuarios u
    where u.id = (select auth.uid())
      and u.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
);

drop policy if exists "Project owners and staff can delete projects" on public.proyectos;
create policy "Project owners and staff can delete projects"
on public.proyectos for delete to authenticated
using (
  creador_id = (select auth.uid())
  or exists (
    select 1 from public.usuarios u
    where u.id = (select auth.uid())
      and u.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
);

drop policy if exists "Users can read project members" on public.proyecto_integrantes;
create policy "Users can read project members"
on public.proyecto_integrantes for select to authenticated
using (
  usuario_id = (select auth.uid())
  or exists (
    select 1 from public.proyectos p
    where p.id = proyecto_id and p.creador_id = (select auth.uid())
  )
  or exists (
    select 1 from public.usuarios u
    where u.id = (select auth.uid())
      and u.rol in ('Profesor', 'Coordinador', 'Administrador')
  )
);

drop policy if exists "Project owners can manage members" on public.proyecto_integrantes;
create policy "Project owners can manage members"
on public.proyecto_integrantes for all to authenticated
using (
  exists (
    select 1 from public.proyectos p
    where p.id = proyecto_id and p.creador_id = (select auth.uid())
  )
)
with check (
  exists (
    select 1 from public.proyectos p
    where p.id = proyecto_id and p.creador_id = (select auth.uid())
  )
);

drop policy if exists "Project owners and staff can access deliverables" on public.entregables;
create policy "Project owners and staff can access deliverables"
on public.entregables for all to authenticated
using (
  exists (
    select 1 from public.proyectos p
    where p.id = proyecto_id
      and (
        p.creador_id = (select auth.uid())
        or exists (
          select 1 from public.usuarios u
          where u.id = (select auth.uid())
            and u.rol in ('Profesor', 'Coordinador', 'Administrador')
        )
      )
  )
)
with check (
  exists (
    select 1 from public.proyectos p
    where p.id = proyecto_id
      and (
        p.creador_id = (select auth.uid())
        or exists (
          select 1 from public.usuarios u
          where u.id = (select auth.uid())
            and u.rol in ('Profesor', 'Coordinador', 'Administrador')
        )
      )
  )
);

insert into public.usuarios (id, nombre_completo, correo_institucional)
select id, coalesce(raw_user_meta_data ->> 'nombre_completo', ''), email
from auth.users
where email is not null
on conflict (id) do update
set correo_institucional = excluded.correo_institucional;

notify pgrst, 'reload schema';
