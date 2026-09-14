using System;

namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2·층 11] 구역 식별자 — AreaSheetSO.areaId 문자열의 값 타입 래퍼.
    // AreaLoader.LoadAsync(areaId)·AreaIdEventChannelSO 페이로드. 문자열 비교(서수)로 동치 — 대소문자 구분.
    // 직렬화 대상이 아니다(readonly) — 시트에는 문자열로 저장되고 AreaSheetSO.Id가 감싼다.
    public readonly struct AreaId : IEquatable<AreaId>
    {
        public readonly string Value;

        public AreaId(string value)
        {
            Value = value ?? "";
        }

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public bool Equals(AreaId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AreaId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        }

        public override string ToString()
        {
            return Value ?? "";
        }

        public static bool operator ==(AreaId a, AreaId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(AreaId a, AreaId b)
        {
            return !a.Equals(b);
        }
    }
}
