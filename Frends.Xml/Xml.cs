﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Saxon.Api;
using Newtonsoft.Json;
#pragma warning disable 1591

namespace Frends.Xml
{
    public class Xml
    {
        /// <summary>
        /// Query XML with XPath and return a list of results. See: https://github.com/FrendsPlatform/Frends.Xml
        /// </summary>
        /// <returns>Object { List &lt;object&gt; Data, List&lt;JToken&gt; ToJson(),JToken ToJson(int index) }</returns>
        public static QueryResults XpathQuery([PropertyTab]QueryInput input, [PropertyTab]QueryOptions options)
        {
            var xPathSelector = SetupXPathSelector(input, options);
            var result = xPathSelector.Evaluate().GetList().Cast<XdmItem>();

            if (options.ThrowErrorOnEmptyResults && !result.Any())
            {
                throw new NullReferenceException($"Could not find any nodes with XPath: {input.XpathQuery}");
            }

            return new QueryResults(result);
        }

        /// <summary>
        /// Query XML with XPath and return a single result. See: https://github.com/FrendsPlatform/Frends.Xml
        /// </summary>
        /// <returns>Object { object Data, JToken ToJson() } </returns>
        public static QuerySingleResults XpathQuerySingle([PropertyTab]QueryInput input, [PropertyTab]QueryOptions options)
        {
            var xPathSelector = SetupXPathSelector(input, options);

            var result = xPathSelector.EvaluateSingle();

            if (options.ThrowErrorOnEmptyResults && result == null)
            {
                throw new NullReferenceException($"Could not find any nodes with XPath: {input.XpathQuery}");
            }

            return new QuerySingleResults(result);
        }

        /// <summary>
        /// Create a XSLT transformation. See: https://github.com/FrendsPlatform/Frends.Xml
        /// </summary>
        /// <returns>string</returns>
        public static string Transform(TransformInput input)
        {
            var processor = new Processor();
            var compiler = processor.NewXsltCompiler();

            using (var stringReader = new StringReader(input.Xslt))
            {
                var executable = compiler.Compile(stringReader);
                var transformer = executable.Load();

                
                using (var inputStream = new MemoryStream())
                {
                    //XmlDocument always produces MemoryStream where its encoding matches the input XML's declaration
                    XmlDocument xmldoc = new XmlDocument();
                    xmldoc.PreserveWhitespace = true;
                    xmldoc.LoadXml(input.Xml);
                    xmldoc.Save(inputStream);
                    xmldoc = null;
                    inputStream.Position = 0;
                    transformer.SetInputStream(inputStream, new Uri("file://"));

                    input.XsltParameters?.ToList().ForEach(x => transformer.SetParameter(new QName(x.Name), new XdmAtomicValue(x.Value)));

                    using (var stringWriter = new StringWriter())
                    {
                        var serializer = processor.NewSerializer();
                        serializer.SetOutputWriter(stringWriter);
                        transformer.Run(serializer);
                        var output = stringWriter.GetStringBuilder().ToString();
                        output = output.Replace("\n", Environment.NewLine);
                        return output;
                    }
                }
            }
        }

        /// <summary>
        /// Validate XML against XML Schema Definitions. See: https://github.com/FrendsPlatform/Frends.Xml
        /// </summary>
        /// <returns>Object { bool IsValid, string Error } </returns>
        public static ValidateResult Validate([PropertyTab]ValidationInput input, [PropertyTab]ValidationOptions options)
        {
            var s = input.Xml as string;
            if (s != null)
            {
                var xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(s);
                return ValidateXmlDocument(xmlDocument, input.XsdSchemas, options);
            }

            var document = input.Xml as XmlDocument;
            if (document != null)
            {
                return ValidateXmlDocument(document, input.XsdSchemas, options);
            }

            throw new InvalidDataException("The input data was not recognized as XML. Supported formats are XML string and XMLDocument.");
        }

        /// <summary>
        /// Convert JSON string to XML string. See: https://github.com/FrendsPlatform/Frends.Xml
        /// </summary>
        /// <returns>string</returns>
        public static string ConvertJsonToXml(JsonToXmlInput input)
        {
            return JsonConvert.DeserializeXmlNode(input.Json, input.XmlRootElementName).OuterXml;
        }

        private static ValidateResult ValidateXmlDocument(XmlDocument xmlDocument, IEnumerable<string> inputXsdSchemas, ValidationOptions options)
        {
            var validateResult = new ValidateResult() {IsValid = true};
            var schemas = new XmlSchemaSet();

            var settings = new XmlReaderSettings {ValidationType = ValidationType.Schema};
            settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;

            foreach (var schema in inputXsdSchemas)
            {
                schemas.Add(null, XmlReader.Create(new StringReader(schema), settings));
            }

            XDocument.Load(new XmlNodeReader(xmlDocument)).Validate(schemas, (o, e) =>
            {

                if (options.ThrowOnValidationErrors)
                {
                    throw new XmlSchemaValidationException(e.Message, e.Exception);
                }
                validateResult.IsValid = false;
                validateResult.Error = e.Message;
            });

            return validateResult;
        }

        private static XPathSelector SetupXPathSelector(QueryInput input, QueryOptions options)
        {
            var proc = new Processor();
            var builder = proc.NewDocumentBuilder();
            builder.SchemaValidationMode = SchemaValidationMode.Lax;

            var xPathCompiler = proc.NewXPathCompiler();
            if (options.XmlNamespaces != null)
            {
                foreach (XmlNamespace ns in options.XmlNamespaces)
                {
                    xPathCompiler.DeclareNamespace(ns.Prefix, ns.Uri);
                }
            }

            switch (options.XpathVersion)
            {
                case XPathVersion.V3:
                    xPathCompiler.XPathLanguageVersion = "3.0";
                    break;
                case XPathVersion.V2:
                    xPathCompiler.XPathLanguageVersion = "2.0";
                    break;
                case XPathVersion.V1:
                    xPathCompiler.XPathLanguageVersion = "1.0";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var xPathSelector = xPathCompiler.Compile(input.XpathQuery).Load();
            builder.BaseUri = new Uri("file://");
            using (var reader = new StringReader(input.Xml))
            {
                var xdmNode = builder.Build(reader);
                xPathSelector.ContextItem = xdmNode;
                return xPathSelector;
            }
        }
    }
}
