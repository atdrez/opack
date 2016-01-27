OPack (Object Pack) Specification
=================================

OPack is an object serialization format, like JSON, but much more faster and smaller.
It's main principle is to provide a extremely compact and efficient way to store and process the same kind of information without any loss.

Performance Comparison
----------------------

Compared to JSON, OPack is both more compact and more efficient to process. Part of this is due to more efficient binary encoding, but it also provides additional features, like key/value indexation and type size reductions, that make it much faster to process and smaller than other types of binary formats like BSON and UBJSON.


Universal Compatibility
------------------------

OPack is fully compatible with JSON. It provides a 1:1 transformation, supporting all types without loosing any information. It also provides other common types not supported by JSON, like byte arrays and date/time values.


License
------------------------
OPack is distributed under the [MIT License](https://opensource.org/licenses/MIT).
Use of the spec, either as-defined or a customized extension of it, is intended to be commercial-friendly.
