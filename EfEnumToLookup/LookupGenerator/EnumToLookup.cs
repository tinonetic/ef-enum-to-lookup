﻿namespace EfEnumToLookup.LookupGenerator
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Data.Entity;
	using System.Data.Entity.Infrastructure;
	using System.Data.SqlClient;
	using System.Linq;
	using System.Reflection;
	using System.Text;
	using System.Text.RegularExpressions;

	/// <summary>
	/// Makes up for a missing feature in Entity Framework 6.1
	/// Creates lookup tables and foreign key constraints based on the enums
	/// used in your model.
	/// Use the properties exposed to control behaviour.
	/// Run <c>Apply</c> from your Seed method in either your database initializer
	/// or your EF Migrations.
	/// It is safe to run repeatedly, and will ensure enum values are kept in line
	/// with your current code.
	/// Source code: https://github.com/timabell/ef-enum-to-lookup
	/// License: MIT
	/// </summary>
	public class EnumToLookup : IEnumToLookup
	{
		public EnumToLookup()
		{
			// set default behaviour, can be overridden by setting properties on object before calling Apply()
			NameFieldLength = 255;
			TableNamePrefix = "Enum_";
			SplitWords = true;
		}

		/// <summary>
		/// If set to true (default) enum names will have spaces inserted between
		/// PascalCase words, e.g. enum SomeValue is stored as "Some Value".
		/// </summary>
		public bool SplitWords { get; set; }

		/// <summary>
		/// The size of the Name field that will be added to the generated lookup tables.
		/// Adjust to suit your data if required, defaults to 255.
		/// </summary>
		public int NameFieldLength { get; set; }

		/// <summary>
		/// Prefix to add to all the generated tables to separate help group them together
		/// and make them stand out as different from other tables.
		/// Defaults to "Enum_" set to null or "" to not have any prefix.
		/// </summary>
		public string TableNamePrefix { get; set; }

		/// <summary>
		/// Suffix to add to all the generated tables to separate help group them together
		/// and make them stand out as different from other tables.
		/// Defaults to "" set to null or "" to not have any suffix.
		/// </summary>
		public string TableNameSuffix { get; set; }

		/// <summary>
		/// Create any missing lookup tables,
		/// enforce values in the lookup tables
		/// by way of a T-SQL MERGE
		/// </summary>
		/// <param name="context">EF Database context to search for enum references,
		///  context.Database.ExecuteSqlCommand() is used to apply changes.</param>
		public void Apply(DbContext context)
		{
			// recurese through dbsets and references finding anything that uses an enum
			var enumReferences = FindEnumReferences(context);

			var sqlServerHandler = new SqlServerHandler
			{
				NameFieldLength = NameFieldLength,
				TableNamePrefix = TableNamePrefix,
				TableNameSuffix = TableNameSuffix,
			};

			// for the list of enums generate tables
			var enums = enumReferences.Select(r => r.EnumType).Distinct().ToList();
			sqlServerHandler.CreateTables(enums, (sql) => context.Database.ExecuteSqlCommand(sql));

			// t-sql merge values into table
			sqlServerHandler.PopulateLookups(enums, (sql, parameters) => context.Database.ExecuteSqlCommand(sql, parameters.Cast<object>().ToArray()));

			// add fks from all referencing tables
			sqlServerHandler.AddForeignKeys(enumReferences, (sql) => context.Database.ExecuteSqlCommand(sql));
		}

		private string EnumName(object value, Type lookup)
		{
			var description = DescriptionValue(value, lookup);
			if (description != null)
			{
				return description;
			}

			var name = value.ToString();

			if (SplitWords)
			{
				return SplitCamelCase(name);
			}
			return name;
		}

		private static string SplitCamelCase(string name)
		{
			// http://stackoverflow.com/questions/773303/splitting-camelcase/25876326#25876326
			name = Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1", RegexOptions.Compiled);
			return name;
		}

		private static string DescriptionValue(object value, Type enumType)
		{
			// https://stackoverflow.com/questions/1799370/getting-attributes-of-enums-value/1799401#1799401
			var member = enumType.GetMember(value.ToString()).First();
			var description = member.GetCustomAttributes(typeof(DescriptionAttribute)).FirstOrDefault() as DescriptionAttribute;
			return description == null ? null : description.Description;
		}

		private static bool IsRuntimeOnly(object value, Type enumType)
		{
			// https://stackoverflow.com/questions/1799370/getting-attributes-of-enums-value/1799401#1799401
			var member = enumType.GetMember(value.ToString()).First();
			return member.GetCustomAttributes(typeof(RuntimeOnlyAttribute)).Any();
		}

		internal IList<EnumReference> FindEnumReferences(DbContext context)
		{
			var metadataWorkspace = ((IObjectContextAdapter)context).ObjectContext.MetadataWorkspace;

			var metadataHandler = new MetadataHandler();
			return metadataHandler.FindEnumReferences(metadataWorkspace);
		}

		internal IList<PropertyInfo> FindDbSets(Type contextType)
		{
			return contextType.GetProperties()
				.Where(p => p.PropertyType.IsGenericType
										&& p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
				.ToList();
		}

		internal IList<PropertyInfo> FindEnums(Type type)
		{
			return type.GetProperties()
				.Where(p => p.PropertyType.IsEnum
										|| (p.PropertyType.IsGenericType && p.PropertyType.GenericTypeArguments.First().IsEnum))
				.ToList();
		}
	}
}
