﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using NewLife.Log;
using NewLife.Security;
using XCode.Model;

namespace XCode.Common
{
    /// <summary>数据模拟</summary>
    /// <typeparam name="T"></typeparam>
    public class DataSimulation<T> : DataSimulation where T : Entity<T>, new()
    {
        /// <summary>实例化</summary>
        public DataSimulation() { Factory = Entity<T>.Meta.Factory; }
    }

    /// <summary>数据模拟</summary>
    public class DataSimulation
    {
        #region 属性
        /// <summary>实体工厂</summary>
        public IEntityOperate Factory { get; set; }

        /// <summary>事务提交的批大小</summary>
        public Int32 BatchSize { get; set; } = 1000;

        /// <summary>并发线程数</summary>
        public Int32 Threads { get; set; } = 1;

        /// <summary>直接执行SQL</summary>
        public Boolean UseSql { get; set; }
        #endregion

        #region 构造
        /// <summary>实例化</summary>
        public DataSimulation()
        {
            //Threads = Environment.ProcessorCount * 3 / 4;
            //if (Threads < 1) Threads = 1;
        }
        #endregion

        #region 方法
        /// <summary>开始执行</summary>
        /// <param name="count"></param>
        public void Run(Int32 count)
        {
            var fact = Factory;
            var pst = XCodeService.Container.ResolveInstance<IEntityPersistence>();

            // 关闭SQL日志
            //XCode.Setting.Current.ShowSQL = false;
            //fact.Session.Dal.Session.ShowSQL = false;
            fact.Session.Dal.Db.ShowSQL = false;
            // 不必获取自增返回值
            fact.AutoIdentity = false;

            // 预热数据表
            WriteLog("{0} 已有数据：{1:n0}", fact.TableName, fact.Count);

            // 准备数据
            var list = new List<IEntity>();
            var qs = new List<String>();

            WriteLog("正在准备数据：");
            var cpu = Environment.ProcessorCount;
            Parallel.For(0, cpu, n =>
            {
                var k = 0;
                for (int i = n; i < count; i += cpu, k++)
                {
                    if (k % BatchSize == 0) Console.Write(".");

                    var e = fact.Create();
                    foreach (var item in fact.Fields)
                    {
                        if (item.IsIdentity) continue;

                        if (item.Type == typeof(Int32))
                            e.SetItem(item.Name, Rand.Next());
                        else if (item.Type == typeof(String))
                            e.SetItem(item.Name, Rand.NextString(8));
                        else if (item.Type == typeof(DateTime))
                            e.SetItem(item.Name, DateTime.Now.AddSeconds(Rand.Next(-10000, 10000)));
                    }
                    var sql = "";
                    if (UseSql) sql = pst.GetSql(e, DataObjectMethodType.Insert);
                    lock (list)
                    {
                        list.Add(e);
                        if (UseSql) qs.Add(sql);
                    }
                }
            });
            Console.WriteLine();
            WriteLog("数据准备完毕！");

            var sw = new Stopwatch();
            sw.Start();

            WriteLog("正在准备写入：");
            var ths = Threads;
            Parallel.For(0, ths, n =>
            {
                var k = 0;
                EntityTransaction tr = null;
                for (int i = n; i < list.Count; i += ths, k++)
                {
                    if (k % BatchSize == 0)
                    {
                        Console.Write(".");
                        if (tr != null) tr.Commit();

                        tr = fact.CreateTrans();
                    }

                    if (!UseSql)
                        list[i].Insert();
                    else
                        fact.Session.Execute(qs[i]);
                }
                if (tr != null) tr.Commit();
            });

            sw.Stop();
            Console.WriteLine();
            WriteLog("数据写入完毕！");
            var ms = sw.ElapsedMilliseconds;
            WriteLog("{2}插入{3:n0}行数据，耗时：{0:n0}ms 速度：{1:n0}tps", ms, list.Count * 1000L / ms, fact.Session.Dal.DbType, list.Count);

            WriteLog("{0} 共有数据：{1:n0}", fact.TableName, fact.Count);
        }
        #endregion

        #region 日志
        /// <summary>日志</summary>
        public ILog Log { get; set; } = Logger.Null;

        /// <summary>写日志</summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        public void WriteLog(String format, params Object[] args)
        {
            Log?.Info(format, args);
        }
        #endregion
    }
}