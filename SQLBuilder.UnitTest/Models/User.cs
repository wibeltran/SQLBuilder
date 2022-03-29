﻿using SQLBuilder.Attributes;

namespace SQLBuilder.UnitTest
{
    [Table("Base_UserInfo")]
    public class UserInfo
    {
        /// <summary>
        /// 主键，且更新实体时，不进行更新
        /// </summary>
        [Key]
        [Column(Update = false)]
        [DataType(IsDbType = true, DbType = System.Data.DbType.Int64)]
        public int? Id { get; set; }
        public int Sex { get; set; }

        [DataType(IsDbType = true, DbType = System.Data.DbType.String)]
        public string Name { get; set; }
        public string Email { get; set; }
    }

    /// <summary>
    /// 查询转换实体
    /// </summary>
    public class UserDto
    {
        [Column("SEX", Format = true)]
        public int Sex { get; set; }
        public string Name { get; set; }
    }

    public class Const
    {
        public static string Name = "张三";
    }
}
