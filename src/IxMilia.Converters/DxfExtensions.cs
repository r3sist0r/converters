using System;
using System.Collections.Generic;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace IxMilia.Converters
{
    public static class DxfExtensions
    {
        public static DxfPoint GetPointFromAngle(this DxfCircle circle, double angleInDegrees)
        {
            var angleInRadians = angleInDegrees * Math.PI / 180.0;
            var sin = Math.Sin(angleInRadians);
            var cos = Math.Cos(angleInRadians);
            return new DxfPoint(cos * circle.Radius, sin * circle.Radius, 0.0) + circle.Center;
        }

        struct LocatedDescription
        {
            public DxfPoint point;
            public string description;
        }

        struct LocatedEntity
        {
            public DxfPoint point;
            public DxfEntity entity;
        }

        public static IEnumerable<Tuple<string,DxfEntity>> AssociateEntitiesWithDescriptions(IEnumerable<DxfEntity> input_entities)
        {
            List<LocatedEntity> graphical_object_locations = new List<LocatedEntity>();
            List<LocatedDescription> desciptions_locations = new List<LocatedDescription>();
            List<Tuple<string, DxfEntity>> ret = new List<Tuple<string, DxfEntity>>();
            foreach (DxfEntity entity in input_entities)
            {
                DxfPoint point = DxfPoint.Origin;
                string desc = null;
                switch (entity.EntityType)
                {
                    case DxfEntityType.MText:
                        DxfMText text = entity as DxfMText;
                        desc = text.Text;
                        point = text.InsertionPoint;
                        break;
                    case DxfEntityType.Text:
                        DxfText text2 = entity as DxfText;
                        desc = text2.Value;
                        point = text2.Location;
                        break;
                    default:
                        var boundingBox = entity.GetBoundingBox().Value;
                        point = (boundingBox.MinimumPoint + boundingBox.MaximumPoint) / 2;
                        break;
                }

                if (desc == null) // graphical object
                {
                    graphical_object_locations.Add(new LocatedEntity { point = point, entity = entity });
                }
                else if (point != DxfPoint.Origin)
                {
                    LocatedDescription md;
                    md.point = point;
                    md.description = desc;
                    desciptions_locations.Add(md);
                }
            }

            if (desciptions_locations.Count == 0)
            {
                foreach (var o in graphical_object_locations)
                    ret.Add(new Tuple<string, DxfEntity>(null, o.entity));
            }
            else
            {
                if (desciptions_locations.Count != graphical_object_locations.Count)
                    throw new InvalidOperationException("number of objects differs from number of descriptions");

                foreach (var b in desciptions_locations)
                {
                    double min_dist = double.MaxValue;
                    LocatedEntity? closest_object = null;
                    foreach (var c in graphical_object_locations)
                    {
                        var distance = (c.point - b.point).Length;
                        if (distance < min_dist)
                        {
                            min_dist = distance;
                            closest_object = c;
                        }
                    }
                    if (closest_object != null)
                    {
                        ret.Add(new Tuple<string, DxfEntity>(b.description, closest_object.Value.entity));
                        graphical_object_locations.Remove((LocatedEntity)closest_object);
                    }
                }
            }
            return ret;
        }
    }
}
