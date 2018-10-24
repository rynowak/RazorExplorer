using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace RazorExplorer.Client
{
    public class Generatrix
    {
        private RazorProjectEngine _engine;
        private RazorConfiguration _configuration;

        public Generatrix()
        {
            _engine = RazorProjectEngine.Create(RazorConfiguration.Default, new EmptyFileSystem(), (b) =>
            {

            });
        }

        public RazorConfiguration Configuration
        {
            get => _configuration;
            set
            {
                _configuration = value;
                _engine = RazorProjectEngine.Create(value, new EmptyFileSystem(), (b) =>
                {

                });
            }
        }

        public string ProcessRuntime(string text)
        {
            var item = new FakeProjectItem(text);
            var codeDocument = _engine.Process(item);
            return codeDocument.GetCSharpDocument().GeneratedCode;
        }

        public string ProcessDesignTime(string text)
        {
            var item = new FakeProjectItem(text);
            var codeDocument = _engine.ProcessDesignTime(item);
            return codeDocument.GetCSharpDocument().GeneratedCode;
        }

        public string ProcessIR(string text)
        {
            var item = new FakeProjectItem(text);
            var codeDocument = _engine.Process(item);
            var ir = codeDocument.GetDocumentIntermediateNode();

            
            var formatter = new DebuggerDisplayFormatter2();
            formatter.FormatTree(ir);
            return formatter.ToString();
        }

        private class FakeProjectItem : RazorProjectItem
        {
            private readonly byte[] _bytes;

            public FakeProjectItem(string text)
            {
                _bytes = Encoding.UTF8.GetBytes(text);
            }

            public override string BasePath => "/";

            public override string FilePath => "/Test.cshtml";

            public override string PhysicalPath => "c:\\text\\Test.cshtml";

            public override bool Exists => true;

            public override Stream Read()
            {
                return new MemoryStream(_bytes);
            }
        }

        private class NotFoundProjectItem : RazorProjectItem
        {
            public override string BasePath => "/";

            public override string FilePath => "/Test.cshtml";

            public override string PhysicalPath => "c:\\text\\Test.cshtml";

            public override bool Exists => false;

            public override Stream Read()
            {
                throw new NotImplementedException();
            }
        }

        private class EmptyFileSystem : RazorProjectFileSystem
        {
            public override IEnumerable<RazorProjectItem> EnumerateItems(string basePath)
            {
                return Array.Empty<RazorProjectItem>();
            }

            public override RazorProjectItem GetItem(string path)
            {
                return new NotFoundProjectItem();
            }
        }

        private class DebuggerDisplayFormatter2 : IntermediateNodeFormatterBase2
        {
            public DebuggerDisplayFormatter2()
            {
                Writer = new StringWriter();
                ContentMode = FormatterContentMode.PreferContent;
            }

            public override string ToString()
            {
                return Writer.ToString();
            }
        }

        internal class IntermediateNodeFormatterBase2 : IntermediateNodeFormatter
        {
            private string _content;
            private Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.Ordinal);

            protected FormatterContentMode ContentMode { get; set; }

            protected bool IncludeSource { get; set; }

            protected TextWriter Writer { get; set; }

            public override void WriteChildren(IntermediateNodeCollection children)
            {
                if (children == null)
                {
                    throw new ArgumentNullException(nameof(children));
                }

                Writer.Write(" ");
                Writer.Write("\"");
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i] as IntermediateToken;
                    if (child != null)
                    {
                        Writer.Write(EscapeNewlines(child.Content));
                    }
                }
                Writer.Write("\"");
            }

            public override void WriteContent(string content)
            {
                if (content == null)
                {
                    return;
                }

                _content = EscapeNewlines(content);
            }

            public override void WriteProperty(string key, string value)
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                if (value == null)
                {
                    return;
                }

                _properties.Add(key, EscapeNewlines(value));
            }

            public void FormatNode(IntermediateNode node)
            {
                if (node == null)
                {
                    throw new ArgumentNullException(nameof(node));
                }

                BeginNode(node);
                node.FormatNode(this);
                EndNode(node);
            }

            public void FormatTree(IntermediateNode node)
            {
                if (node == null)
                {
                    throw new ArgumentNullException(nameof(node));
                }

                var visitor = new FormatterVisitor(this);
                visitor.Visit(node);
            }

            private void BeginNode(IntermediateNode node)
            {
                Writer.Write(GetShortName(node));

                if (IncludeSource)
                {
                    Writer.Write(" ");
                    Writer.Write(node.Source?.ToString() ?? "(n/a)");
                }
            }

            private void EndNode(IntermediateNode node)
            {
                if (_content != null && (_properties.Count == 0 || ContentMode == FormatterContentMode.PreferContent))
                {
                    Writer.Write(" ");
                    Writer.Write("\"");
                    Writer.Write(EscapeNewlines(_content));
                    Writer.Write("\"");
                }

                if (_properties.Count > 0 && (_content == null || ContentMode == FormatterContentMode.PreferProperties))
                {
                    Writer.Write(" ");
                    Writer.Write("{ ");
                    Writer.Write(string.Join(", ", _properties.Select(kvp => $"{kvp.Key}: \"{kvp.Value}\"")));
                    Writer.Write(" }");
                }

                _content = null;
                _properties.Clear();
            }

            private string GetShortName(IntermediateNode node)
            {
                var typeName = node.GetType().Name;
                return
                    typeName.EndsWith(nameof(IntermediateNode), StringComparison.Ordinal) ?
                    typeName.Substring(0, typeName.Length - nameof(IntermediateNode).Length) :
                    typeName;
            }

            private string EscapeNewlines(string content)
            {
                return content.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            }

            // Depending on the usage of the formatter we might prefer thoroughness (properties)
            // or brevity (content). Generally if a node has a single string that provides value
            // it has content.
            //
            // Some nodes have neither: TagHelperBody
            // Some nodes have content: HtmlContent
            // Some nodes have properties: Document
            // Some nodes have both: TagHelperProperty
            protected enum FormatterContentMode
            {
                PreferContent,
                PreferProperties,
            }

            protected class FormatterVisitor : IntermediateNodeWalker
            {
                private const int IndentSize = 2;

                private readonly IntermediateNodeFormatterBase2 _formatter;
                private int _indent = 0;

                public FormatterVisitor(IntermediateNodeFormatterBase2 formatter)
                {
                    _formatter = formatter;
                }

                public override void VisitDefault(IntermediateNode node)
                {
                    // Indent
                    for (var i = 0; i < _indent; i++)
                    {
                        _formatter.Writer.Write(' ');
                    }
                    _formatter.FormatNode(node);
                    _formatter.Writer.WriteLine();

                    // Process children
                    _indent += IndentSize;
                    base.VisitDefault(node);
                    _indent -= IndentSize;
                }
            }
        }
    }
}
