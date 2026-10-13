begin;

do $$
declare
  auth_record record;
  previous_profile_id uuid;
  profile_role text;
  fk_record record;
begin
  for auth_record in
    select id, email, raw_user_meta_data
    from auth.users
    where lower(email) in ('estudiante@sigpa.edu', 'profesor@sigpa.edu')
  loop
    profile_role := case
      when lower(auth_record.email) = 'profesor@sigpa.edu' then 'Profesor'
      else 'Estudiante'
    end;

    select id
    into previous_profile_id
    from public.usuarios
    where lower(correo_institucional) = lower(auth_record.email)
      and id <> auth_record.id
    limit 1;

    if previous_profile_id is not null then
      update public.usuarios
      set correo_institucional = 'migrated-' || previous_profile_id::text || '@invalid.sigpa'
      where id = previous_profile_id;
    end if;

    insert into public.usuarios (id, nombre_completo, correo_institucional, rol)
    values (
      auth_record.id,
      coalesce(
        auth_record.raw_user_meta_data ->> 'nombre_completo',
        split_part(auth_record.email, '@', 1)
      ),
      auth_record.email,
      profile_role
    )
    on conflict (id) do update
      set nombre_completo = excluded.nombre_completo,
          correo_institucional = excluded.correo_institucional,
          rol = excluded.rol;

    if previous_profile_id is not null then
      for fk_record in
        select
          referencing_namespace.nspname as schema_name,
          referencing_table.relname as table_name,
          referencing_column.attname as column_name
        from pg_constraint foreign_key
        join pg_class referencing_table
          on referencing_table.oid = foreign_key.conrelid
        join pg_namespace referencing_namespace
          on referencing_namespace.oid = referencing_table.relnamespace
        join lateral unnest(foreign_key.conkey) with ordinality as referencing_key(attnum, position)
          on true
        join lateral unnest(foreign_key.confkey) with ordinality as referenced_key(attnum, position)
          on referenced_key.position = referencing_key.position
        join pg_attribute referencing_column
          on referencing_column.attrelid = foreign_key.conrelid
         and referencing_column.attnum = referencing_key.attnum
        join pg_attribute referenced_column
          on referenced_column.attrelid = foreign_key.confrelid
         and referenced_column.attnum = referenced_key.attnum
        where foreign_key.contype = 'f'
          and foreign_key.confrelid = 'public.usuarios'::regclass
          and referenced_column.attname = 'id'
      loop
        execute format(
          'update %I.%I set %I = $1 where %I = $2',
          fk_record.schema_name,
          fk_record.table_name,
          fk_record.column_name,
          fk_record.column_name
        )
        using auth_record.id, previous_profile_id;
      end loop;

      delete from public.usuarios
      where id = previous_profile_id;
    end if;

    previous_profile_id := null;
  end loop;
end;
$$;

notify pgrst, 'reload schema';

commit;
