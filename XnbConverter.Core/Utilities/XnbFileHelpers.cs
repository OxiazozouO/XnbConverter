﻿using XnbConverter.Entity;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Xact;
using static XnbConverter.Readers.TypeReader;
using Texture2D = XnbConverter.Entity.Mono.Texture2D;

namespace XnbConverter.Utilities;

public static class XnbFileHelpers
{
    /**
     * 用于保存解析后的XNB文件。
     * @param {object} xnbObject
     * @returns {Boolean}
     */
    public static bool ExportFile(this XNB xnb, string filename)
    {
        XnbObject? xnbObject = xnb.XnbConfig;
        object? data = xnb.Data;
        // 获取文件的目录名
        string dirname = Path.GetDirectoryName(filename);
        
        // 如果目录不存在，则创建目录路径
        if (!Directory.Exists(dirname))
            Directory.CreateDirectory(dirname);
        
        // 确保xnbObject对象中存在content字段
        if (xnbObject is null || xnbObject.Content is null)
            throw new XnbError("无效的对象！");
        // 获取content字段的引用
        var content = xnbObject.Content;
        // var found = content.SelectToken("$..export");
        
        // 如果找到要导出的数据   非纯json的格式，包含图片，音频，地图等
        if (data != null)
        {
            if (content is null  || data is null)
                throw new XnbError("无效的文件导出！");
        
            // 记录正在导出的附加数据
            Log.Info("正在导出{0}...", content.Extension);
            
            // 获取文件的基本名称
            string basename = Path.GetFileNameWithoutExtension(filename);
            var ext = content.Extension.Split(' ');
            string[] paths = new string[ext.Length];
            for (var i = 0; i < ext.Length; i++)
            {
                paths[i] = Path.Combine(dirname, basename + ext[i]);
            }
            
            // 根据导出类型进行切换
            switch (content.Extension)
            {
                // Texture2D转为PNG
                case /* 630 */Ext.TEXTURE_2D :
                    var texture2D = (Texture2D)data;
                    texture2D.SaveAsPng(paths[0]);
                    content.Format = texture2D.Format;
                break;
                
                //Json
                case /* 376 */Ext.JSON :
                    data.ToJson(paths[0]);
                    content.Format = 0;
                    break;
                
                //tbin and tmx
                case /* 212 */Ext.TBIN :
                    // 保存文件数据
                    TBin10 tbin = (TBin10)data;
                    File.WriteAllBytes(paths[0], tbin.Data);
                    break;
                
                //SpriteFont
                case /*  6  */Ext.SPRITE_FONT :
                    var spriteFont = (SpriteFont)data;
                    content.Format = spriteFont.Texture.Format;
                    spriteFont.Save(paths[0],paths[1]);
                break;
                
                // 编译后的Effect
                case /*  3  */Ext.EFFECT :
                    // 保存文件数据
                    File.WriteAllBytes(paths[0], ((Effect)data).Data);
                break;
                
                // BmFont Xml
                case /*  4  */Ext.BM_FONT :
                    // 保存文件数据
                    File.WriteAllText(paths[0], ((BmFont)data).Data);
                break;
                case Ext.SoundEffect:
                    var sound = (SoundEffect)data;
                    sound.Save(paths[0], paths[1]);
                break;
            }
        }
        // 将XNB对象信息保存为JSON
        xnbObject.ToJson(filename);
        // 成功导出文件
        return true;
    }

    /**
     * 将所有导出的内容解析回对象中
     * @param {String} filename
     * @returns {Object}
     */
    public static void ResolveImports(this XNB xnb, string filename)
    {
        // 读取XNB配置文件
        XnbObject? json = FileUtils.ToEntity<XnbObject>(filename);
        if (json?.Content is null)
            throw new XnbError("{0} 缺少 'content' 字段.", filename);
        var content = json.Content;
        var ext = content.Extension.Split(' ');
        var paths = new string[ext.Length];
        // 组成导出文件的路径
        for (var i = 0; i < ext.Length; i++)
        {
            paths[i] = Path.ChangeExtension(filename, ext[i]);
        }

        object? data = null;
        // 根据支持的文件扩展名进行切换
        switch (content.Extension)
        {
            // Texture2D转为PNG
            case Ext.TEXTURE_2D:
                // 获取PNG数据
                var texture2D = Texture2D.FromPng(paths[0]);
                texture2D.Format = json.Content.Format;
                data = texture2D;
                break;
            // 编译后的Effect
            case Ext.EFFECT:
                data = new Effect{
                    Data = File.ReadAllBytes(paths[0])
                };
                break;
            // TBin地图
            case Ext.TBIN:
                TBin10 tbin = TBin10.FormTbin(paths[0]);
                data = tbin;
                break;
            // BmFont Xml
            case Ext.BM_FONT:
                data = new BmFont{
                    Data = File.ReadAllText(paths[0])
                };
                break;
            case Ext.SPRITE_FONT:
                // 获取PNG数据
                var spriteFont = SpriteFont.FormFiles(paths[0], paths[1]);
                spriteFont.Texture.Format = json.Content.Format;
                data = spriteFont;
                break;
            case Ext.JSON:
                data = File.ReadAllText(paths[0]);
                break;
            case Ext.SoundEffect:
                var soundEffect = SoundEffect.FormWave(paths[0], paths[1]);
                data = soundEffect;
                break;
        }

        xnb.Data = data;
        xnb.XnbConfig = json;
    }
}