-- Migracion aditiva: ejecutar despues de schema.sql.
-- No elimina ni renombra columnas/tablas existentes.

alter table public.proyectos
  add column if not exists fecha_correccion_hasta timestamptz,
  add column if not exists problema text not null default '',
  add column if not exists objetivo_general text not null default '';

alter table public.proyecto_integrantes
  add column if not exists estado text not null default 'Aceptado',
  add column if not exists invitado_por uuid references public.usuarios(id) on delete set null,
  add column if not exists respondido_at timestamptz;

do $$
begin
  if not exists (
    select 1 from pg_constraint
    where conname = 'proyecto_integrantes_estado_check'
      and conrelid = 'public.proyecto_integrantes'::regclass
  ) then
    alter table public.proyecto_integrantes
      add constraint proyecto_integrantes_estado_check
      check (estado in ('Invitado', 'Aceptado', 'Rechazado', 'Retirado')) not valid;
  end if;
end;
$$;

create index if not exists proyecto_integrantes_usuario_estado_idx
  on public.proyecto_integrantes(usuario_id, estado);

create or replace function public.validar_integrante_proyecto()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  curso_proyecto text;
  integrantes_activos integer;
  rol_usuario text;
  creador_proyecto uuid;
begin
  if new.estado not in ('Invitado', 'Aceptado') then
    return new;
  end if;

  select curso, creador_id into curso_proyecto, creador_proyecto
  from public.proyectos
  where id = new.proyecto_id;
  if curso_proyecto is null then
    raise exception 'La asignatura/proyecto no existe';
  end if;

  perform pg_advisory_xact_lock(hashtext(new.usuario_id::text || ':' || lower(curso_proyecto)));
  perform pg_advisory_xact_lock(hashtext('proyecto:' || new.proyecto_id::text));

  select rol into rol_usuario from public.usuarios where id = new.usuario_id;
  if rol_usuario is distinct from 'Estudiante' then
    raise exception 'Solo estudiantes pueden pertenecer a un equipo';
  end if;

  if exists (
    select 1
    from public.proyecto_integrantes pi
    join public.proyectos p on p.id = pi.proyecto_id
    where pi.usuario_id = new.usuario_id
      and pi.proyecto_id <> new.proyecto_id
      and pi.estado in ('Invitado', 'Aceptado')
      and lower(p.curso) = lower(curso_proyecto)
  ) then
    raise exception 'El estudiante ya pertenece a un equipo de esta asignatura (RN-02)';
  end if;

  select count(*) into integrantes_activos
  from public.proyecto_integrantes
  where proyecto_id = new.proyecto_id
    and estado in ('Invitado', 'Aceptado')
    and id <> new.id;
  if not exists (
    select 1 from public.proyecto_integrantes
    where proyecto_id = new.proyecto_id
      and usuario_id = creador_proyecto
      and estado = 'Aceptado'
      and id <> new.id
  ) then
    integrantes_activos := integrantes_activos + 1;
  end if;
  if integrantes_activos >= 5 then
    raise exception 'El equipo ya alcanzó el límite máximo de 5 integrantes (RN-02)';
  end if;

  return new;
end;
$$;

drop trigger if exists proyecto_integrantes_validar on public.proyecto_integrantes;
create trigger proyecto_integrantes_validar
before insert or update of proyecto_id, usuario_id, estado
on public.proyecto_integrantes
for each row execute function public.validar_integrante_proyecto();

alter table public.entregables
  add column if not exists permite_entrega_extemporanea boolean not null default false,
  add column if not exists penalizacion_extemporanea_pct numeric(5,2) not null default 0,
  add column if not exists penalizacion_aplicada_pct numeric(5,2) not null default 0;

do $$
begin
  if not exists (
    select 1 from pg_constraint
    where conname = 'entregables_penalizacion_extemporanea_pct_check'
      and conrelid = 'public.entregables'::regclass
  ) then
    alter table public.entregables
      add constraint entregables_penalizacion_extemporanea_pct_check
      check (penalizacion_extemporanea_pct between 0 and 100) not valid;
  end if;
  if not exists (
    select 1 from pg_constraint
    where conname = 'entregables_penalizacion_aplicada_pct_check'
      and conrelid = 'public.entregables'::regclass
  ) then
    alter table public.entregables
      add constraint entregables_penalizacion_aplicada_pct_check
      check (penalizacion_aplicada_pct between 0 and 100) not valid;
  end if;
end;
$$;

create table if not exists public.objetivos_especificos (
  id uuid primary key default gen_random_uuid(),
  proyecto_id uuid not null references public.proyectos(id) on delete cascade,
  descripcion text not null check (length(trim(descripcion)) > 0),
  orden integer not null check (orden > 0),
  created_at timestamptz not null default now(),
  unique (proyecto_id, orden)
);

create table if not exists public.riesgos (
  id uuid primary key default gen_random_uuid(),
  proyecto_id uuid not null references public.proyectos(id) on delete cascade,
  reportado_por uuid not null references public.usuarios(id) on delete restrict,
  titulo text not null check (length(trim(titulo)) > 0),
  descripcion text not null check (length(trim(descripcion)) > 0),
  severidad text not null default 'Media'
    check (severidad in ('Baja', 'Media', 'Alta', 'Crítica')),
  estado text not null default 'Abierto'
    check (estado in ('Abierto', 'En seguimiento', 'Resuelto')),
  created_at timestamptz not null default now(),
  resuelto_at timestamptz
);

create table if not exists public.entregas_versiones (
  id uuid primary key default gen_random_uuid(),
  entregable_id uuid not null references public.entregables(id) on delete restrict,
  enviada_por uuid not null references public.usuarios(id) on delete restrict,
  archivo_url text not null check (length(trim(archivo_url)) > 0),
  enviada_at timestamptz not null default now(),
  estado text not null check (estado in ('Entregado a Tiempo', 'Entregado Tardío')),
  penalizacion_aplicada_pct numeric(5,2) not null default 0
    check (penalizacion_aplicada_pct between 0 and 100)
);

create index if not exists entregas_versiones_hito_fecha_idx
  on public.entregas_versiones(entregable_id, enviada_at desc);

create index if not exists riesgos_proyecto_fecha_idx
  on public.riesgos(proyecto_id, created_at desc);

create table if not exists public.eventos_auditoria (
  id uuid primary key default gen_random_uuid(),
  actor_id uuid,
  accion text not null,
  recurso_tipo text not null,
  recurso_id uuid not null,
  ocurrido_at timestamptz not null default now(),
  correlation_id text not null default '',
  datos jsonb not null default '{}'::jsonb
);

create index if not exists eventos_auditoria_recurso_fecha_idx
  on public.eventos_auditoria(recurso_tipo, recurso_id, ocurrido_at desc);
create index if not exists eventos_auditoria_fecha_idx
  on public.eventos_auditoria(ocurrido_at);

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('entregables', 'entregables', false, 26214400, array['application/pdf', 'application/zip'])
on conflict (id) do update
set public = false,
    file_size_limit = 26214400,
    allowed_mime_types = array['application/pdf', 'application/zip'];

create or replace function public.bloquear_cambios_auditoria()
returns trigger
language plpgsql
as $$
begin
  raise exception 'Los eventos de auditoría son inmutables';
end;
$$;

drop trigger if exists eventos_auditoria_inmutables on public.eventos_auditoria;
create trigger eventos_auditoria_inmutables
before update or delete on public.eventos_auditoria
for each row execute function public.bloquear_cambios_auditoria();

create table if not exists public.tareas_kanban (
  id uuid primary key default gen_random_uuid(),
  proyecto_id uuid not null references public.proyectos(id) on delete cascade,
  titulo text not null check (length(trim(titulo)) > 0),
  descripcion text not null default '',
  responsable_id uuid not null references public.usuarios(id) on delete restrict,
  fecha_vencimiento timestamptz,
  estado text not null default 'Por Hacer'
    check (estado in ('Por Hacer', 'En Proceso', 'Hecho', 'Completado')),
  orden integer not null default 0,
  version integer not null default 1 check (version > 0)
);

alter table public.tareas_kanban
  add column if not exists descripcion text not null default '';

-- En tablas preexistentes evita bloquear la migracion por tarjetas antiguas
-- incompletas, pero impide nuevas filas/actualizaciones sin responsable.
do $$
begin
  if not exists (
    select 1 from pg_constraint
    where conname = 'tareas_kanban_responsable_obligatorio'
      and conrelid = 'public.tareas_kanban'::regclass
  ) then
    alter table public.tareas_kanban
      add constraint tareas_kanban_responsable_obligatorio
      check (responsable_id is not null) not valid;
  end if;
end;
$$;

create table if not exists public.criterios_rubrica (
  id uuid primary key default gen_random_uuid(),
  entregable_id uuid not null references public.entregables(id) on delete cascade,
  nombre text not null check (length(trim(nombre)) > 0),
  descripcion text not null default '',
  peso_porcentual numeric(5,2) not null
    check (peso_porcentual > 0 and peso_porcentual <= 100)
);

create table if not exists public.niveles_rubrica (
  id uuid primary key default gen_random_uuid(),
  criterio_id uuid not null references public.criterios_rubrica(id) on delete cascade,
  nivel integer not null check (nivel > 0),
  etiqueta text not null check (length(trim(etiqueta)) > 0),
  descripcion text not null default '',
  puntaje numeric(5,2) not null check (puntaje between 0 and 100),
  unique (criterio_id, nivel)
);

create or replace function public.validar_peso_rubrica()
returns trigger
language plpgsql
as $$
declare
  entregable_uuid uuid;
  total_peso numeric(7,2);
begin
  for entregable_uuid in
    select distinct target.entregable_id
    from unnest(array[
      case when tg_op <> 'DELETE' then new.entregable_id else null end,
      case when tg_op <> 'INSERT' then old.entregable_id else null end
    ]) as target(entregable_id)
    where target.entregable_id is not null
  loop
    if not exists (
      select 1 from public.entregables where id = entregable_uuid
    ) then
      continue;
    end if;

    select coalesce(sum(peso_porcentual), 0)
      into total_peso
      from public.criterios_rubrica
      where entregable_id = entregable_uuid;

    if total_peso <> 100.00 then
      raise exception 'La suma de los pesos de la rubrica del entregable % debe ser 100%% (actual: %)', entregable_uuid, total_peso;
    end if;
  end loop;
  return null;
end;
$$;

drop trigger if exists criterios_rubrica_peso_total on public.criterios_rubrica;
create constraint trigger criterios_rubrica_peso_total
after insert or update or delete on public.criterios_rubrica
deferrable initially deferred
for each row execute function public.validar_peso_rubrica();

create table if not exists public.evaluaciones (
  id uuid primary key default gen_random_uuid(),
  entregable_id uuid not null unique references public.entregables(id) on delete restrict,
  evaluador_id uuid not null references public.usuarios(id) on delete restrict,
  puntaje_final numeric(5,2) not null check (puntaje_final between 0 and 100),
  observacion_general text not null default '',
  publicada boolean not null default false,
  evaluada_at timestamptz not null default now(),
  publicada_at timestamptz,
  check ((publicada = false and publicada_at is null) or
         (publicada = true and publicada_at is not null))
);

create table if not exists public.evaluacion_criterios (
  id uuid primary key default gen_random_uuid(),
  evaluacion_id uuid not null references public.evaluaciones(id) on delete cascade,
  criterio_id uuid not null references public.criterios_rubrica(id) on delete restrict,
  nivel_id uuid not null references public.niveles_rubrica(id) on delete restrict,
  puntaje_obtenido numeric(5,2) not null check (puntaje_obtenido between 0 and 100),
  observacion text not null default '',
  unique (evaluacion_id, criterio_id)
);

create index if not exists evaluaciones_entregable_fecha_idx
  on public.evaluaciones(entregable_id, evaluada_at desc);

create or replace function public.bloquear_edicion_calificacion_publicada()
returns trigger
language plpgsql
as $$
begin
  if old.publicada then
    raise exception 'Una calificación publicada es inmutable';
  end if;
  return new;
end;
$$;

drop trigger if exists evaluaciones_publicadas_inmutables on public.evaluaciones;
create trigger evaluaciones_publicadas_inmutables
before update on public.evaluaciones
for each row execute function public.bloquear_edicion_calificacion_publicada();

alter table public.evaluaciones enable row level security;
alter table public.evaluacion_criterios enable row level security;
alter table public.tareas_kanban enable row level security;
alter table public.criterios_rubrica enable row level security;
revoke all on public.evaluaciones, public.evaluacion_criterios from public, anon, authenticated;
grant select on public.evaluaciones, public.evaluacion_criterios to authenticated;
alter table public.niveles_rubrica enable row level security;
revoke all on public.tareas_kanban, public.criterios_rubrica, public.niveles_rubrica from public, anon, authenticated;
grant select, insert, update on public.tareas_kanban to authenticated;
grant select on public.criterios_rubrica to authenticated;
grant select on public.niveles_rubrica to authenticated;

drop policy if exists "Teams see published grades and staff see all" on public.evaluaciones;
create policy "Teams see published grades and staff see all"
on public.evaluaciones for select to authenticated
using (
  publicada
  and exists (
    select 1 from public.entregables e
    join public.proyectos p on p.id = e.proyecto_id
    where e.id = evaluaciones.entregable_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = p.id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado'))
  )
  or exists (select 1 from public.usuarios u
             where u.id = (select auth.uid())
               and u.rol in ('Profesor', 'Coordinador', 'Administrador'))
);

drop policy if exists "Teachers can manage evaluations" on public.evaluaciones;
create policy "Teachers can manage evaluations"
on public.evaluaciones for all to authenticated
using (exists (select 1 from public.usuarios u
               where u.id = (select auth.uid()) and u.rol = 'Profesor'))
with check (exists (select 1 from public.usuarios u
                    where u.id = (select auth.uid()) and u.rol = 'Profesor'));

drop policy if exists "Teams see authorized evaluation details" on public.evaluacion_criterios;
create policy "Teams see authorized evaluation details"
on public.evaluacion_criterios for select to authenticated
using (
  exists (
    select 1 from public.evaluaciones evaluation
    where evaluation.id = evaluacion_criterios.evaluacion_id
      and evaluation.publicada
      and exists (
        select 1 from public.entregables e
        join public.proyectos p on p.id = e.proyecto_id
        where e.id = evaluation.entregable_id
          and (p.creador_id = (select auth.uid())
            or exists (select 1 from public.proyecto_integrantes pi
                       where pi.proyecto_id = p.id
                         and pi.usuario_id = (select auth.uid())
                         and pi.estado = 'Aceptado'))
      )
  )
  or exists (select 1 from public.usuarios u
             where u.id = (select auth.uid())
               and u.rol in ('Profesor', 'Coordinador', 'Administrador'))
);

drop policy if exists "Teachers can insert evaluation details" on public.evaluacion_criterios;
create policy "Teachers can insert evaluation details"
on public.evaluacion_criterios for insert to authenticated
with check (exists (select 1 from public.usuarios u
                    where u.id = (select auth.uid()) and u.rol = 'Profesor'));

drop policy if exists "Project teams and staff can read kanban tasks" on public.tareas_kanban;
create policy "Project teams and staff can read kanban tasks"
on public.tareas_kanban for select to authenticated
using (
  exists (
    select 1 from public.proyectos p
    where p.id = tareas_kanban.proyecto_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = p.id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado')
        or exists (select 1 from public.usuarios u
                   where u.id = (select auth.uid())
                     and u.rol in ('Profesor', 'Coordinador', 'Administrador')))
  )
);

drop policy if exists "Project teams can insert kanban tasks" on public.tareas_kanban;
create policy "Project teams can insert kanban tasks"
on public.tareas_kanban for insert to authenticated
with check (
  exists (select 1 from public.proyectos p
          where p.id = tareas_kanban.proyecto_id
            and (p.creador_id = (select auth.uid())
              or exists (select 1 from public.proyecto_integrantes pi
                         where pi.proyecto_id = p.id
                           and pi.usuario_id = (select auth.uid())
                           and pi.estado = 'Aceptado')))
  and exists (select 1 from public.proyecto_integrantes pi
              where pi.proyecto_id = tareas_kanban.proyecto_id
                and pi.usuario_id = tareas_kanban.responsable_id
                and pi.estado = 'Aceptado')
);

drop policy if exists "Project teams can update kanban tasks" on public.tareas_kanban;
create policy "Project teams can update kanban tasks"
on public.tareas_kanban for update to authenticated
using (
  exists (select 1 from public.proyecto_integrantes pi
          where pi.proyecto_id = tareas_kanban.proyecto_id
            and pi.usuario_id = (select auth.uid())
            and pi.estado = 'Aceptado')
  or exists (select 1 from public.proyectos p
             where p.id = tareas_kanban.proyecto_id
               and p.creador_id = (select auth.uid()))
)
with check (
  exists (select 1 from public.proyecto_integrantes pi
          where pi.proyecto_id = tareas_kanban.proyecto_id
            and pi.usuario_id = tareas_kanban.responsable_id
            and pi.estado = 'Aceptado')
);

drop policy if exists "Teams and staff can read rubric criteria" on public.criterios_rubrica;
create policy "Teams and staff can read rubric criteria"
on public.criterios_rubrica for select to authenticated
using (
  exists (
    select 1 from public.entregables e
    join public.proyectos p on p.id = e.proyecto_id
    where e.id = criterios_rubrica.entregable_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = p.id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado')
        or exists (select 1 from public.usuarios u
                   where u.id = (select auth.uid())
                     and u.rol in ('Profesor', 'Coordinador', 'Administrador')))
  )
);

drop policy if exists "Teachers can manage rubric criteria" on public.criterios_rubrica;
create policy "Teachers can manage rubric criteria"
on public.criterios_rubrica for all to authenticated
using (exists (select 1 from public.usuarios u
               where u.id = (select auth.uid()) and u.rol = 'Profesor'))
with check (exists (select 1 from public.usuarios u
                    where u.id = (select auth.uid()) and u.rol = 'Profesor'));

drop policy if exists "Teams and staff can read rubric levels" on public.niveles_rubrica;
create policy "Teams and staff can read rubric levels"
on public.niveles_rubrica for select to authenticated
using (
  exists (
    select 1 from public.criterios_rubrica criterion
    join public.entregables e on e.id = criterion.entregable_id
    join public.proyectos p on p.id = e.proyecto_id
    where criterion.id = niveles_rubrica.criterio_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = p.id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado')
        or exists (select 1 from public.usuarios u
                   where u.id = (select auth.uid())
                     and u.rol in ('Profesor', 'Coordinador', 'Administrador')))
  )
);

alter table public.objetivos_especificos enable row level security;
alter table public.riesgos enable row level security;
alter table public.eventos_auditoria enable row level security;
alter table public.entregas_versiones enable row level security;

revoke all on public.objetivos_especificos, public.riesgos, public.eventos_auditoria
from public, anon;
revoke all on public.entregas_versiones from public, anon, authenticated;
grant select, insert, update, delete on public.objetivos_especificos to authenticated;
grant select, insert on public.riesgos to authenticated;
revoke update, delete on public.riesgos from authenticated;
grant select on public.entregas_versiones to authenticated;
grant select on public.eventos_auditoria to authenticated;

drop policy if exists "Project teams and staff can read objectives" on public.objetivos_especificos;
create policy "Project teams and staff can read objectives"
on public.objetivos_especificos for select to authenticated
using (
  exists (
    select 1 from public.proyectos p
    where p.id = objetivos_especificos.proyecto_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = objetivos_especificos.proyecto_id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado')
        or exists (select 1 from public.usuarios u
                   where u.id = (select auth.uid())
                     and u.rol in ('Profesor', 'Coordinador', 'Administrador')))
  )
);

drop policy if exists "Project teams can manage objectives" on public.objetivos_especificos;
create policy "Project teams can manage objectives"
on public.objetivos_especificos for all to authenticated
using (
  exists (select 1 from public.proyecto_integrantes pi
          where pi.proyecto_id = objetivos_especificos.proyecto_id
            and pi.usuario_id = (select auth.uid())
            and pi.estado = 'Aceptado')
  or exists (select 1 from public.proyectos p
             where p.id = objetivos_especificos.proyecto_id
               and p.creador_id = (select auth.uid()))
)
with check (
  exists (select 1 from public.proyecto_integrantes pi
          where pi.proyecto_id = objetivos_especificos.proyecto_id
            and pi.usuario_id = (select auth.uid())
            and pi.estado = 'Aceptado')
  or exists (select 1 from public.proyectos p
             where p.id = objetivos_especificos.proyecto_id
               and p.creador_id = (select auth.uid()))
);

drop policy if exists "Project teams and staff can read risks" on public.riesgos;
create policy "Project teams and staff can read risks"
on public.riesgos for select to authenticated
using (
  exists (
    select 1 from public.proyectos p
    where p.id = riesgos.proyecto_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = riesgos.proyecto_id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado')
        or exists (select 1 from public.usuarios u
                   where u.id = (select auth.uid())
                     and u.rol in ('Profesor', 'Coordinador', 'Administrador')))
  )
);

drop policy if exists "Project teams can report risks" on public.riesgos;
create policy "Project teams can report risks"
on public.riesgos for insert to authenticated
with check (
  reportado_por = (select auth.uid())
  and exists (
    select 1 from public.proyectos p
    where p.id = riesgos.proyecto_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = riesgos.proyecto_id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado'))
  )
);

drop policy if exists "Risk reporters and staff can update risks" on public.riesgos;

drop policy if exists "Administrators can read audit events" on public.eventos_auditoria;
create policy "Administrators can read audit events"
on public.eventos_auditoria for select to authenticated
using (
  exists (select 1 from public.usuarios u
          where u.id = (select auth.uid()) and u.rol = 'Administrador')
);

drop policy if exists "Teams and staff can read submission history" on public.entregas_versiones;
create policy "Teams and staff can read submission history"
on public.entregas_versiones for select to authenticated
using (
  exists (
    select 1 from public.entregables e
    join public.proyectos p on p.id = e.proyecto_id
    where e.id = entregas_versiones.entregable_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = p.id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado')
        or exists (select 1 from public.usuarios u
                   where u.id = (select auth.uid())
                     and u.rol in ('Profesor', 'Coordinador', 'Administrador')))
  )
);

drop policy if exists "Invitees can read their own invitations" on public.proyecto_integrantes;
create policy "Invitees can read their own invitations"
on public.proyecto_integrantes for select to authenticated
using (usuario_id = (select auth.uid()) and estado = 'Invitado');

drop policy if exists "Project owners can manage members" on public.proyecto_integrantes;
revoke insert, update, delete on public.proyecto_integrantes from authenticated;

-- Las escrituras de proyectos se reservan a la API, que valida rol, objetivos,
-- membresía, transacciones y auditoría antes de persistir.
drop policy if exists "Project owners and staff can update projects" on public.proyectos;
drop policy if exists "Owners and staff can update projects" on public.proyectos;
revoke insert, update, delete on public.proyectos from authenticated;
drop policy if exists "Teachers can update project review status" on public.proyectos;

drop policy if exists "Students can create their own projects" on public.proyectos;
drop policy if exists "Authenticated users can create their own projects" on public.proyectos;

drop policy if exists "Project teams can read projects" on public.proyectos;
drop policy if exists "Students and staff can read projects" on public.proyectos;
create policy "Project teams can read projects"
on public.proyectos for select to authenticated
using (
  creador_id = (select auth.uid())
  or exists (select 1 from public.proyecto_integrantes pi
             where pi.proyecto_id = proyectos.id
               and pi.usuario_id = (select auth.uid())
               and pi.estado = 'Aceptado')
  or exists (select 1 from public.usuarios u
             where u.id = (select auth.uid())
               and u.rol in ('Profesor', 'Coordinador', 'Administrador'))
);

-- La API valida pertenencia antes de exponer entregas. El cliente directo solo
-- puede leer entregas de su equipo; evita editar fechas, pesos y calificaciones.
drop policy if exists "Project owners and staff can access deliverables" on public.entregables;
revoke insert, update, delete on public.entregables from authenticated;
grant select on public.entregables to authenticated;
create policy "Project teams and staff can read deliverables"
on public.entregables for select to authenticated
using (
  exists (
    select 1 from public.proyectos p
    where p.id = entregables.proyecto_id
      and (p.creador_id = (select auth.uid())
        or exists (select 1 from public.proyecto_integrantes pi
                   where pi.proyecto_id = p.id
                     and pi.usuario_id = (select auth.uid())
                     and pi.estado = 'Aceptado')
        or exists (select 1 from public.usuarios u
                   where u.id = (select auth.uid())
                     and u.rol in ('Profesor', 'Coordinador', 'Administrador')))
  )
);

notify pgrst, 'reload schema';
