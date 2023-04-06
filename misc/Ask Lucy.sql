/****** Script for SelectTopNRows command from SSMS  ******/
Use[db_a15752_asklucy]
GO

DELETE
--SELECT *
  FROM [db_a15752_asklucy].[dbo].[AspNetUsers]
  WHERE [Email] = 'mustafa.salaheldin@hotmail.com'
SELECT [Id]
      ,[UserName]
      ,[NormalizedUserName]
      ,[Email]
      ,[NormalizedEmail]
      ,[EmailConfirmed]
      ,[PasswordHash]
      ,[SecurityStamp]
      ,[ConcurrencyStamp]
      ,[PhoneNumber]
      ,[PhoneNumberConfirmed]
      ,[TwoFactorEnabled]
      ,[LockoutEnd]
      ,[LockoutEnabled]
      ,[AccessFailedCount]
  FROM [db_a15752_asklucy].[dbo].[AspNetUsers]