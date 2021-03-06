﻿// Copyright (c) 2012-2016 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

namespace Dicom
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    using Dicom.IO.Buffer;
    using Dicom.StructuredReport;


    /// <summary>
    /// A collection of <see cref="DicomItem">DICOM items</see>.
    /// </summary>
    public class DicomDataset : IEnumerable<DicomItem>
    {
        #region FIELDS

        private IDictionary<DicomTag, DicomItem> _items;

        private DicomTransferSyntax _syntax;

        #endregion

        #region CONSTRUCTORS

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataset"/> class.
        /// </summary>
        public DicomDataset()
        {
            _items = new SortedDictionary<DicomTag, DicomItem>();
            InternalTransferSyntax = DicomTransferSyntax.ExplicitVRLittleEndian;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataset"/> class.
        /// </summary>
        /// <param name="items">An array of DICOM items.</param>
        public DicomDataset(params DicomItem[] items)
            : this((IEnumerable<DicomItem>)items)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataset"/> class.
        /// </summary>
        /// <param name="items">A collection of DICOM items.</param>
        public DicomDataset(IEnumerable<DicomItem> items)
            : this()
        {
            if (items != null)
            {
                foreach (var item in items.Where(item => item != null))
                {
                    if (item.ValueRepresentation.Equals(DicomVR.SQ))
                    {
                        var tag = item.Tag;
                        var sequenceItems =
                            ((DicomSequence)item).Items.Where(dataset => dataset != null)
                                .Select(dataset => new DicomDataset(dataset))
                                .ToArray();
                        _items[tag] = new DicomSequence(tag, sequenceItems);
                    }
                    else
                    {
                        _items[item.Tag] = item;
                    }
                }
            }
        }

        #endregion

        #region PROPERTIES

        /// <summary>Gets the DICOM transfer syntax of this dataset.</summary>
        public DicomTransferSyntax InternalTransferSyntax
        {
            get
            {
                return _syntax;
            }
            internal set
            {
                _syntax = value;

                // update transfer syntax for sequence items
                foreach (var sq in this.Where(x => x.ValueRepresentation == DicomVR.SQ).Cast<DicomSequence>())
                {
                    foreach (var item in sq.Items)
                    {
                        item.InternalTransferSyntax = _syntax;
                    }
                }
            }
        }

        #endregion

        #region METHODS

        /// <summary>
        /// Gets the item or element value of the specified <paramref name="tag"/>. 
        /// </summary>
        /// <typeparam name="T">Type of the return value.</typeparam>
        /// <param name="tag">Requested DICOM tag.</param>
        /// <param name="n">Item index (for multi-valued elements).</param>
        /// <returns>Item or element value corresponding to <paramref name="tag"/>.</returns>
        /// <exception cref="DicomDataException">If the dataset does not contain <paramref name="tag"/> or if the specified 
        /// <paramref name="n">item index</paramref> is out-of-range.</exception>
        public T Get<T>(DicomTag tag, int n = 0)
        {
            return Get<T>(tag, n, false, default(T));
        }

        /// <summary>
        /// Gets the integer element value of the specified <paramref name="tag"/>, or default value if dataset does not contain <paramref name="tag"/>. 
        /// </summary>
        /// <param name="tag">Requested DICOM tag.</param>
        /// <param name="defaultValue">Default value to apply if <paramref name="tag"/> is not contained in dataset.</param>
        /// <returns>Element value corresponding to <paramref name="tag"/>.</returns>
        /// <exception cref="DicomDataException">If the element corresponding to <paramref name="tag"/> cannot be converted to an integer.</exception>
        public int Get(DicomTag tag, int defaultValue)
        {
            return Get<int>(tag, 0, true, defaultValue);
        }

        /// <summary>
        /// Gets the item or element value of the specified <paramref name="tag"/>, or default value if dataset does not contain <paramref name="tag"/>. 
        /// </summary>
        /// <typeparam name="T">Type of the return value.</typeparam>
        /// <param name="tag">Requested DICOM tag.</param>
        /// <param name="defaultValue">Default value to apply if <paramref name="tag"/> is not contained in dataset.</param>
        /// <returns>Item or element value corresponding to <paramref name="tag"/>.</returns>
        /// <remarks>In code, consider to use this method with implicit type specification, since <typeparamref name="T"/> can be inferred from
        /// <paramref name="defaultValue"/>, e.g. prefer <code>dataset.Get(tag, "Default")</code> over <code>dataset.Get&lt;string&gt;(tag, "Default")</code>.</remarks>
        public T Get<T>(DicomTag tag, T defaultValue)
        {
            return Get<T>(tag, 0, true, defaultValue);
        }

        /// <summary>
        /// Gets the item or element value of the specified <paramref name="tag"/>, or default value if dataset does not contain <paramref name="tag"/>. 
        /// </summary>
        /// <typeparam name="T">Type of the return value.</typeparam>
        /// <param name="tag">Requested DICOM tag.</param>
        /// <param name="n">Item index (for multi-valued elements).</param>
        /// <param name="defaultValue">Default value to apply if <paramref name="tag"/> is not contained in dataset.</param>
        /// <returns>Item or element value corresponding to <paramref name="tag"/>.</returns>
        public T Get<T>(DicomTag tag, int n, T defaultValue)
        {
            return Get<T>(tag, n, true, defaultValue);
        }

        /// <summary>
        /// Converts a dictionary tag to a valid private tag. Creates the private creator tag if needed.
        /// </summary>
        /// <param name="tag">Dictionary DICOM tag</param>
        /// <returns>Private DICOM tag</returns>
        public DicomTag GetPrivateTag(DicomTag tag)
        {
            // not a private tag
            if (!tag.IsPrivate) return tag;

            // group length
            if (tag.Element == 0x0000) return tag;

            // private creator?
            if (tag.PrivateCreator == null) return tag;

            // already a valid private tag
            if (tag.Element >= 0xff) return tag;

            ushort group = 0x0010;
            for (;; group++)
            {
                var creator = new DicomTag(tag.Group, group);
                if (!Contains(creator))
                {
                    Add(new DicomLongString(creator, tag.PrivateCreator.Creator));
                    break;
                }

                var value = Get<string>(creator, String.Empty);
                if (tag.PrivateCreator.Creator == value) return new DicomTag(tag.Group, (ushort)((group << 8) + (tag.Element & 0xff)), tag.PrivateCreator);
            }

            return new DicomTag(tag.Group, (ushort)((group << 8) + (tag.Element & 0xff)), tag.PrivateCreator);
        }

        /// <summary>
        /// Add a collection of DICOM items to the dataset (replace existing).
        /// </summary>
        /// <param name="items">Collection of DICOM items to add.</param>
        /// <returns>The dataset instance.</returns>
        public DicomDataset Add(params DicomItem[] items)
        {
            if (items != null)
            {
                foreach (DicomItem item in items)
                {
                    if (item != null)
                    {
                        if (item.Tag.IsPrivate) _items[GetPrivateTag(item.Tag)] = item;
                        else _items[item.Tag] = item;
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Add a collection of DICOM items to the dataset (replace existing).
        /// </summary>
        /// <param name="items">Collection of DICOM items to add.</param>
        /// <returns>The dataset instance.</returns>
        public DicomDataset Add(IEnumerable<DicomItem> items)
        {
            if (items != null)
            {
                foreach (DicomItem item in items)
                {
                    if (item != null)
                    {
                        if (item.Tag.IsPrivate) _items[GetPrivateTag(item.Tag)] = item;
                        else _items[item.Tag] = item;
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Add single DICOM item given by <paramref name="tag"/> and <paramref name="values"/>. Replace existing item.
        /// </summary>
        /// <typeparam name="T">Type of added values.</typeparam>
        /// <param name="tag">DICOM tag of the added item.</param>
        /// <param name="values">Values of the added item.</param>
        /// <returns>The dataset instance.</returns>
        public DicomDataset Add<T>(DicomTag tag, params T[] values)
        {
            var entry = DicomDictionary.Default[tag];
            if (entry == null)
                throw new DicomDataException(
                    "Tag {0} not found in DICOM dictionary. Only dictionary tags may be added implicitly to the dataset.",
                    tag);

            DicomVR vr = null;
            if (values != null) vr = entry.ValueRepresentations.FirstOrDefault(x => x.ValueType == typeof(T));
            if (vr == null) vr = entry.ValueRepresentations.First();

            return Add(vr, tag, values);
        }

        /// <summary>
        /// Add single DICOM item given by <paramref name="vr"/>, <paramref name="tag"/> and <paramref name="values"/>. Replace existing item.
        /// </summary>
        /// <typeparam name="T">Type of added values.</typeparam>
        /// <param name="vr">DICOM vr of the added item. Use when setting a private element.</param>
        /// <param name="tag">DICOM tag of the added item.</param>
        /// <param name="values">Values of the added item.</param>
        /// <remarks>No validation is performed on the <paramref name="vr"/> matching the element <paramref name="tag"/>
        /// This method is useful when adding a private tag and need to explicitly set the VR of the created element.
        /// </remarks>
        /// <returns>The dataset instance.</returns>
        public DicomDataset Add<T>(DicomVR vr, DicomTag tag, params T[] values)
        {
            if (vr == DicomVR.AE)
            {
                if (values == null) return Add(new DicomApplicationEntity(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomApplicationEntity(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.AS)
            {
                if (values == null) return Add(new DicomAgeString(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomAgeString(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.AT)
            {
                if (values == null) return Add(new DicomAttributeTag(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(DicomTag)) return Add(new DicomAttributeTag(tag, values.Cast<DicomTag>().ToArray()));

                IEnumerable<DicomTag> parsedValues;
                if (ParseVrValueFromString(values, tag.DictionaryEntry.ValueMultiplicity, DicomTag.Parse, out parsedValues))
                {
                    return Add(new DicomAttributeTag(tag, parsedValues.ToArray()));
                }
            }

            if (vr == DicomVR.CS)
            {
                if (values == null) return Add(new DicomCodeString(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomCodeString(tag, values.Cast<string>().ToArray()));
                if (typeof(T).GetTypeInfo().IsEnum) return Add(new DicomCodeString(tag, values.Select(x => x.ToString()).ToArray()));
            }

            if (vr == DicomVR.DA)
            {
                if (values == null) return Add(new DicomDate(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(DateTime)) return Add(new DicomDate(tag, values.Cast<DateTime>().ToArray()));
                if (typeof(T) == typeof(DicomDateRange))
                    return
                        Add(new DicomDate(tag, values.Cast<DicomDateRange>().FirstOrDefault() ?? new DicomDateRange()));
                if (typeof(T) == typeof(string)) return Add(new DicomDate(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.DS)
            {
                if (values == null) return Add(new DicomDecimalString(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(decimal)) return Add(new DicomDecimalString(tag, values.Cast<decimal>().ToArray()));
                if (typeof(T) == typeof(string)) return Add(new DicomDecimalString(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.DT)
            {
                if (values == null) return Add(new DicomDateTime(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(DateTime)) return Add(new DicomDateTime(tag, values.Cast<DateTime>().ToArray()));
                if (typeof(T) == typeof(DicomDateRange))
                    return
                        Add(
                            new DicomDateTime(
                                tag,
                                values.Cast<DicomDateRange>().FirstOrDefault() ?? new DicomDateRange()));
                if (typeof(T) == typeof(string)) return Add(new DicomDateTime(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.FD)
            {
                if (values == null) return Add(new DicomFloatingPointDouble(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(double)) return Add(new DicomFloatingPointDouble(tag, values.Cast<double>().ToArray()));

                IEnumerable<double> parsedValues;
                if (ParseVrValueFromString(values, tag.DictionaryEntry.ValueMultiplicity, double.Parse, out parsedValues))
                {
                    return Add(new DicomFloatingPointDouble(tag, parsedValues.ToArray()));
                }              
            }

            if (vr == DicomVR.FL)
            {
                if (values == null) return Add(new DicomFloatingPointSingle(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(float)) return Add(new DicomFloatingPointSingle(tag, values.Cast<float>().ToArray()));

                IEnumerable<float> parsedValues;
                if (ParseVrValueFromString(values, tag.DictionaryEntry.ValueMultiplicity, float.Parse, out parsedValues))
                {
                    return Add(new DicomFloatingPointSingle(tag, parsedValues.ToArray()));
                }
            }

            if (vr == DicomVR.IS)
            {
                if (values == null) return Add(new DicomIntegerString(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(int)) return Add(new DicomIntegerString(tag, values.Cast<int>().ToArray()));
                if (typeof(T) == typeof(string)) return Add(new DicomIntegerString(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.LO)
            {
                if (values == null) return Add(new DicomLongString(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomLongString(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.LT)
            {
                if (values == null) return Add(new DicomLongText(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomLongText(tag, values.Cast<string>().First()));
            }

            if (vr == DicomVR.OB)
            {
                if (values == null) return Add(new DicomOtherByte(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(byte)) return Add(new DicomOtherByte(tag, values.Cast<byte>().ToArray()));
                
                if (typeof(T) == typeof(IByteBuffer) && values.Length == 1)
                {
                    return Add(new DicomOtherByte(tag, (IByteBuffer)values[0]));
                }
            }

            if (vr == DicomVR.OD)
            {
                if (values == null) return Add(new DicomOtherDouble(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(double)) return Add(new DicomOtherDouble(tag, values.Cast<double>().ToArray()));
            
                if (typeof(T) == typeof(IByteBuffer) && values.Length == 1)
                {
                    return Add(new DicomOtherDouble(tag, (IByteBuffer)values[0]));
                }
            }

            if (vr == DicomVR.OF)
            {
                if (values == null) return Add(new DicomOtherFloat(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(float)) return Add(new DicomOtherFloat(tag, values.Cast<float>().ToArray()));

                if (typeof(T) == typeof(IByteBuffer) && values.Length == 1)
                {
                    return Add(new DicomOtherFloat(tag, (IByteBuffer)values[0]));
                }
            }

            if (vr == DicomVR.OL)
            {
                if (values == null) return Add(new DicomOtherLong(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(uint)) return Add(new DicomOtherLong(tag, values.Cast<uint>().ToArray()));

                if (typeof(T) == typeof(IByteBuffer) && values.Length == 1)
                {
                    return Add(new DicomOtherLong(tag, (IByteBuffer)values[0]));
                }
            }

            if (vr == DicomVR.OW)
            {
                if (values == null) return Add(new DicomOtherWord(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(ushort)) return Add(new DicomOtherWord(tag, values.Cast<ushort>().ToArray()));
                
                if (typeof(T) == typeof(IByteBuffer) && values.Length == 1)
                {
                    return Add(new DicomOtherWord(tag, (IByteBuffer)values[0]));
                }
            }

            if (vr == DicomVR.PN)
            {
                if (values == null) return Add(new DicomPersonName(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomPersonName(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.SH)
            {
                if (values == null) return Add(new DicomShortString(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomShortString(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.SL)
            {
                if (values == null) return Add(new DicomSignedLong(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(int)) return Add(new DicomSignedLong(tag, values.Cast<int>().ToArray()));

                IEnumerable<int> parsedValues;
                if ( ParseVrValueFromString(values, tag.DictionaryEntry.ValueMultiplicity, int.Parse, out parsedValues))
                {
                    return Add(new DicomSignedLong(tag, parsedValues.ToArray()));
                }
            }

            if (vr == DicomVR.SQ)
            {
                if (values == null) return Add(new DicomSequence(tag));
                if (typeof(T) == typeof(DicomContentItem)) return Add(new DicomSequence(tag, values.Cast<DicomContentItem>().Select(x => x.Dataset).ToArray()));
                if (typeof(T) == typeof(DicomDataset) || typeof(T) == typeof(DicomCodeItem)
                    || typeof(T) == typeof(DicomMeasuredValue) || typeof(T) == typeof(DicomReferencedSOP)) return Add(new DicomSequence(tag, values.Cast<DicomDataset>().ToArray()));
            }

            if (vr == DicomVR.SS)
            {
                if (values == null) return Add(new DicomSignedShort(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(short)) return Add(new DicomSignedShort(tag, values.Cast<short>().ToArray()));

                IEnumerable<short> parsedValues;
                if (ParseVrValueFromString(values, tag.DictionaryEntry.ValueMultiplicity, short.Parse, out parsedValues))
                {
                    return Add(new DicomSignedShort(tag, parsedValues.ToArray()));
                }
            }

            if (vr == DicomVR.ST)
            {
                if (values == null) return Add(new DicomShortText(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomShortText(tag, values.Cast<string>().First()));
            }

            if (vr == DicomVR.TM)
            {
                if (values == null) return Add(new DicomTime(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(DateTime)) return Add(new DicomTime(tag, values.Cast<DateTime>().ToArray()));
                if (typeof(T) == typeof(DicomDateRange))
                    return
                        Add(new DicomTime(tag, values.Cast<DicomDateRange>().FirstOrDefault() ?? new DicomDateRange()));
                if (typeof(T) == typeof(string)) return Add(new DicomTime(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.UC)
            {
                if (values == null) return Add(new DicomUnlimitedCharacters(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomUnlimitedCharacters(tag, values.Cast<string>().ToArray()));
            }

            if (vr == DicomVR.UI)
            {
                if (values == null) return Add(new DicomUniqueIdentifier(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomUniqueIdentifier(tag, values.Cast<string>().ToArray()));
                if (typeof(T) == typeof(DicomUID)) return Add(new DicomUniqueIdentifier(tag, values.Cast<DicomUID>().ToArray()));
                if (typeof(T) == typeof(DicomTransferSyntax)) return Add(new DicomUniqueIdentifier(tag, values.Cast<DicomTransferSyntax>().ToArray()));
            }

            if (vr == DicomVR.UL)
            {
                if (values == null) return Add(new DicomUnsignedLong(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(uint)) return Add(new DicomUnsignedLong(tag, values.Cast<uint>().ToArray()));

                IEnumerable<uint> parsedValues;
                if (ParseVrValueFromString(values, tag.DictionaryEntry.ValueMultiplicity, uint.Parse, out parsedValues))
                {
                    return Add(new DicomUnsignedLong(tag, parsedValues.ToArray()));
                }
            }

            if (vr == DicomVR.UN)
            {
                if (values == null) return Add(new DicomUnknown(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(byte)) return Add(new DicomUnknown(tag, values.Cast<byte>().ToArray()));
                
                if (typeof(T) == typeof(IByteBuffer) && values.Length == 1)
                {
                    return Add(new DicomUnknown(tag, (IByteBuffer)values[0]));
                }
            }

            if (vr == DicomVR.UR)
            {
                if (values == null) return Add(new DicomUniversalResource(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomUniversalResource(tag, values.Cast<string>().First()));
            }

            if (vr == DicomVR.US)
            {
                if (values == null) return Add(new DicomUnsignedShort(tag, EmptyBuffer.Value));
                if (typeof(T) == typeof(ushort)) return Add(new DicomUnsignedShort(tag, values.Cast<ushort>().ToArray()));

                IEnumerable<ushort> parsedValues;
                if ( ParseVrValueFromString(values, tag.DictionaryEntry.ValueMultiplicity, ushort.Parse, out parsedValues))
                {
                    return Add(new DicomUnsignedShort(tag, parsedValues.ToArray()));
                }
            }

            if (vr == DicomVR.UT)
            {
                if (values == null) return Add(new DicomUnlimitedText(tag, DicomEncoding.Default, EmptyBuffer.Value));
                if (typeof(T) == typeof(string)) return Add(new DicomUnlimitedText(tag, values.Cast<string>().First()));
            }

            throw new InvalidOperationException(
                string.Format(
                    "Unable to create DICOM element of type {0} with values of type {1}",
                    vr.Code,
                    typeof(T)));
        }

        /// <summary>
        /// Checks the DICOM dataset to determine if the dataset already contains an item with the specified tag.
        /// </summary>
        /// <param name="tag">DICOM tag to test</param>
        /// <returns><c>True</c> if a DICOM item with the specified tag already exists.</returns>
        public bool Contains(DicomTag tag)
        {
            return _items.ContainsKey(tag);
        }

        /// <summary>
        /// Removes items for specified tags.
        /// </summary>
        /// <param name="tags">DICOM tags to remove</param>
        /// <returns>Current Dataset</returns>
        public DicomDataset Remove(params DicomTag[] tags)
        {
            foreach (DicomTag tag in tags) _items.Remove(tag);
            return this;
        }

        /// <summary>
        /// Removes items where the selector function returns true.
        /// </summary>
        /// <param name="selector">Selector function</param>
        /// <returns>Current Dataset</returns>
        public DicomDataset Remove(Func<DicomItem, bool> selector)
        {
            foreach (DicomItem item in _items.Values.Where(selector).ToArray()) _items.Remove(item.Tag);
            return this;
        }

        /// <summary>
        /// Removes all items from the dataset.
        /// </summary>
        /// <returns>Current Dataset</returns>
        public DicomDataset Clear()
        {
            _items.Clear();
            return this;
        }

        /// <summary>
        /// Copies all items to the destination dataset.
        /// </summary>
        /// <param name="destination">Destination Dataset</param>
        /// <returns>Current Dataset</returns>
        public DicomDataset CopyTo(DicomDataset destination)
        {
            if (destination != null) destination.Add(this);
            return this;
        }

        /// <summary>
        /// Copies tags to the destination dataset.
        /// </summary>
        /// <param name="destination">Destination Dataset</param>
        /// <param name="tags">Tags to copy</param>
        /// <returns>Current Dataset</returns>
        public DicomDataset CopyTo(DicomDataset destination, params DicomTag[] tags)
        {
            if (destination != null)
            {
                foreach (var tag in tags) destination.Add(Get<DicomItem>(tag));
            }
            return this;
        }

        /// <summary>
        /// Copies tags matching mask to the destination dataset.
        /// </summary>
        /// <param name="destination">Destination Dataset</param>
        /// <param name="mask">Tags to copy</param>
        /// <returns>Current Dataset</returns>
        public DicomDataset CopyTo(DicomDataset destination, DicomMaskedTag mask)
        {
            if (destination != null) destination.Add(_items.Values.Where(x => mask.IsMatch(x.Tag)));
            return this;
        }

        /// <summary>
        /// Enumerates all DICOM items.
        /// </summary>
        /// <returns>Enumeration of DICOM items</returns>
        public IEnumerator<DicomItem> GetEnumerator()
        {
            return _items.Values.GetEnumerator();
        }

        /// <summary>
        /// Enumerates all DICOM items.
        /// </summary>
        /// <returns>Enumeration of DICOM items</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _items.Values.GetEnumerator();
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString()
        {
            return String.Format("DICOM Dataset [{0} items]", _items.Count);
        }

        /// <summary>
        /// Gets the item or element value of the specified <paramref name="tag"/>. 
        /// </summary>
        /// <typeparam name="T">Type of the return value.</typeparam>
        /// <param name="tag">Requested DICOM tag.</param>
        /// <param name="n">Item index (for multi-valued elements).</param>
        /// <param name="useDefault">Indicates whether to use default value (true) or throw (false) if <paramref name="tag"/> is not contained in dataset.</param>
        /// <param name="defaultValue">Default value to apply if <paramref name="tag"/> is not contained in dataset and <paramref name="useDefault"/> is true.</param>
        /// <returns>Item or element value corresponding to <paramref name="tag"/>.</returns>
        private T Get<T>(DicomTag tag, int n, bool useDefault, T defaultValue)
        {
            DicomItem item = null;
            if (!_items.TryGetValue(tag, out item))
            {
                if (useDefault) return defaultValue;
                throw new DicomDataException("Tag: {0} not found in dataset", tag);
            }

            if (typeof(T) == typeof(DicomItem)) return (T)(object)item;

            if (typeof(T).GetTypeInfo().IsSubclassOf(typeof(DicomItem))) return (T)(object)item;

            if (typeof(T) == typeof(DicomVR)) return (T)(object)item.ValueRepresentation;

            if (item.GetType().GetTypeInfo().IsSubclassOf(typeof(DicomElement)))
            {
                DicomElement element = (DicomElement)item;

                if (typeof(IByteBuffer).GetTypeInfo().IsAssignableFrom(typeof(T).GetTypeInfo())) return (T)(object)element.Buffer;

                if (typeof(T) == typeof(byte[])) return (T)(object)element.Buffer.Data;

                if (n >= element.Count || element.Count == 0)
                {
                    if (useDefault) return defaultValue;
                    throw new DicomDataException("Element empty or index: {0} exceeds element count: {1}", n, element.Count);
                }

                return (T)(object)element.Get<T>(n);
            }

            if (item.GetType() == typeof(DicomSequence))
            {
                if (typeof(T) == typeof(DicomCodeItem)) return (T)(object)new DicomCodeItem((DicomSequence)item);

                if (typeof(T) == typeof(DicomMeasuredValue)) return (T)(object)new DicomMeasuredValue((DicomSequence)item);

                if (typeof(T) == typeof(DicomReferencedSOP)) return (T)(object)new DicomReferencedSOP((DicomSequence)item);
            }

            throw new DicomDataException(
                "Unable to get a value type of {0} from DICOM item of type {1}",
                typeof(T),
                item.GetType());
        }

        private static bool ParseVrValueFromString<T, TOut>(
            IEnumerable<T> values,
            DicomVM valueMultiplicity,
            Func<string, TOut> parser,
            out IEnumerable<TOut> parsedValues)
        {
            parsedValues = null;

            if (typeof(T) == typeof(string))
            {
                var stringValues = values.Cast<string>().ToArray();

                if (valueMultiplicity.Maximum > 1 && stringValues.Length == 1)
                {
                    stringValues = stringValues[0].Split('\\');
                }

                parsedValues = stringValues.Where(n => !string.IsNullOrEmpty(n?.Trim())).Select(parser);

                return true;
            }

            return false;
        }

        #endregion
    }
}
