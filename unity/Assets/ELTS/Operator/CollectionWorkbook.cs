#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace Elts.Operator
{
    /// <summary>Small native XLSX exporter for the offline Windows player. No
    /// Excel installation is needed. Text is always an inline string, so participant
    /// names, identifiers and notes can never become spreadsheet formulas.</summary>
    internal static class CollectionWorkbook
    {
        internal sealed class Sheet
        {
            public readonly string Name;public readonly string[] Columns;public readonly string[][] Rows;
            public Sheet(string name,string[] columns,string[][] rows){Name=name;Columns=columns;Rows=rows;}
        }
        private const string Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly HashSet<string> TextColumns=new HashSet<string>{"participantCode","participantName","sessionId","blockId","condition","status","reason","metricsVersion","scoreStatus","outcome","referenceTargetId","referencePolicy","Metric","Unit","Definition"};
        public static void Write(string destination,IReadOnlyList<Sheet> sheets)
        {
            string pending=destination+".pending";
            try
            {
                using(var file=new FileStream(pending,FileMode.CreateNew,FileAccess.Write))
                using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
                {
                    Entry(zip,"[Content_Types].xml",w=> {
                        w.WriteStartElement("Types","http://schemas.openxmlformats.org/package/2006/content-types");
                        Empty(w,"Default","Extension","rels","ContentType","application/vnd.openxmlformats-package.relationships+xml");
                        Empty(w,"Default","Extension","xml","ContentType","application/xml");
                        Empty(w,"Override","PartName","/xl/workbook.xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                        Empty(w,"Override","PartName","/xl/styles.xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                        for(int i=0;i<sheets.Count;i++)Empty(w,"Override","PartName","/xl/worksheets/sheet"+(i+1)+".xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                        w.WriteEndElement();
                    });
                    Entry(zip,"_rels/.rels",w=>{w.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");Relationship(w,"rId1","officeDocument","xl/workbook.xml");w.WriteEndElement();});
                    Entry(zip,"xl/workbook.xml",w=> {
                        w.WriteStartElement("workbook",Main);w.WriteStartElement("sheets");
                        for(int i=0;i<sheets.Count;i++){w.WriteStartElement("sheet");w.WriteAttributeString("name",sheets[i].Name);w.WriteAttributeString("sheetId",(i+1).ToString());w.WriteAttributeString("r","id","http://schemas.openxmlformats.org/officeDocument/2006/relationships","rId"+(i+1));w.WriteEndElement();}
                        w.WriteEndElement();w.WriteEndElement();
                    });
                    Entry(zip,"xl/_rels/workbook.xml.rels",w=> {
                        w.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");
                        for(int i=0;i<sheets.Count;i++)Relationship(w,"rId"+(i+1),"worksheet","worksheets/sheet"+(i+1)+".xml");
                        Relationship(w,"rIdStyles","styles","styles.xml");w.WriteEndElement();
                    });
                    Entry(zip,"xl/styles.xml",w=>WriteXml(w,"<styleSheet xmlns=\""+Main+"\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF173F4A\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"center\"/></xf><xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>"));
                    for(int i=0;i<sheets.Count;i++){var sheet=sheets[i];Entry(zip,"xl/worksheets/sheet"+(i+1)+".xml",w=>WriteSheet(w,sheet));}
                }
                File.Move(pending,destination);
            }
            catch {if(File.Exists(pending))File.Delete(pending);throw;}
        }
        private static void WriteSheet(XmlWriter w,Sheet sheet)
        {
            if(sheet.Rows.Length>1048575)throw new IOException("Excel row limit exceeded. Export the SQLite database to retain the full collection.");
            w.WriteStartElement("worksheet",Main);
            w.WriteStartElement("sheetViews");w.WriteStartElement("sheetView");w.WriteAttributeString("workbookViewId","0");Empty(w,"pane","ySplit","1","topLeftCell","A2","activePane","bottomLeft","state","frozen");w.WriteEndElement();w.WriteEndElement();
            w.WriteStartElement("cols");
            for(int c=0;c<sheet.Columns.Length;c++)Empty(w,"col","min",(c+1).ToString(),"max",(c+1).ToString(),"width",sheet.Columns[c]=="Definition"?"110":TextColumns.Contains(sheet.Columns[c])?"26":"20","customWidth","1");
            w.WriteEndElement();w.WriteStartElement("sheetData");
            for(int r=0;r<=sheet.Rows.Length;r++)
            {
                w.WriteStartElement("row");w.WriteAttributeString("r",(r+1).ToString());
                if(r==0){w.WriteAttributeString("ht","38");w.WriteAttributeString("customHeight","1");}
                for(int c=0;c<sheet.Columns.Length;c++)
                {
                    string text=r==0?Header(sheet.Columns[c]):sheet.Rows[r-1][c];
                    if(text.Length==0)continue; // SQL NULL stays blank, not zero.
                    if(text.Length>32767)throw new IOException("Excel cell limit exceeded. Use the database export for full text.");
                    w.WriteStartElement("c");w.WriteAttributeString("r",Column(c)+(r+1));
                    bool number=r>0 && !TextColumns.Contains(sheet.Columns[c]) && Double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double parsed) && !Double.IsNaN(parsed) && !Double.IsInfinity(parsed);
                    w.WriteAttributeString("s",r==0?"1":number && text.Contains(".")?"2":"0");
                    if(number)w.WriteElementString("v",text);
                    else {w.WriteAttributeString("t","inlineStr");w.WriteStartElement("is");w.WriteStartElement("t");w.WriteAttributeString("xml","space",null,"preserve");w.WriteString(text);w.WriteEndElement();w.WriteEndElement();}
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();Empty(w,"autoFilter","ref","A1:"+Column(sheet.Columns.Length-1)+(sheet.Rows.Length+1));w.WriteEndElement();
        }
        private static void WriteXml(XmlWriter writer,string xml) { using(var reader=XmlReader.Create(new StringReader(xml)))writer.WriteNode(reader,true); }
        private static string Header(string key)
        {
            string label=System.Text.RegularExpressions.Regex.Replace(key,"([a-z])([A-Z])","$1 $2")
                .Replace("Deg Per Second","(deg/s)").Replace("Deg2","(deg²)").Replace("Deg","(deg)")
                .Replace("Mm","(mm)").Replace("Percent","(%)").Replace("Per Second","/s");
            return Char.ToUpperInvariant(label[0])+label.Substring(1);
        }
        private static string Column(int index){string value="";for(int n=index+1;n>0;n=(n-1)/26)value=(char)('A'+(n-1)%26)+value;return value;}
        private static void Relationship(XmlWriter w,string id,string type,string target)=>Empty(w,"Relationship","Id",id,"Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/"+type,"Target",target);
        private static void Empty(XmlWriter w,string name,params string[] attributes){w.WriteStartElement(name);for(int i=0;i<attributes.Length;i+=2)w.WriteAttributeString(attributes[i],attributes[i+1]);w.WriteEndElement();}
        private static void Entry(ZipArchive zip,string name,Action<XmlWriter> write)
        {using(var stream=zip.CreateEntry(name).Open())using(var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false)})){writer.WriteStartDocument();write(writer);writer.WriteEndDocument();}}
    }
}
