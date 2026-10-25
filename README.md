# Unity Mesh Tools

Unity 2022.3 LTS와 Unity 6에서 사용하는 공용 스킨드 메시 절단 편집기 패키지입니다.

## 1.1.1 설치

GitHub private 저장소에 접근할 수 있는 계정으로 Git 인증을 준비하고,
Unity Package Manager에서 다음 URL을 설치합니다.
인증 토큰을 설치 URL이나 프로젝트 파일에 넣지 않습니다.

```text
https://github.com/iruril/unity-mesh-tools.git?path=/Packages/com.mesh-tools.skinned-mesh-cutter#v1.1.1
```

| Unity | 프로젝트의 ProBuilder 버전 |
| --- | --- |
| 2022.3 LTS | 5.2.4 |
| 6.3 LTS | 6.1.2 |

패키지는 ProBuilder 5.2.4를 최소 의존성으로 선언합니다. Unity 6 프로젝트는
ProBuilder 6.1.2를 직접 설치하여 유지합니다. Unity API 차이는 버전 분기로 처리합니다.

## 래그돌 생성기

1. 프로젝트에 Bz Ragdoll Helper가 설치되어 있으면 해당 설치본의 생성 API를 우선 사용합니다.
2. Bz가 없으면 에디터에 포함된 Unity Ragdoll Builder를 사용합니다.
3. 기존 래그돌 참조를 복사하는 기능은 두 생성기 설치 여부와 관계없이 동작합니다.

래그돌 생성기는 프로젝트에 설치된 도구를 호출합니다.
Bz는 선택 사항이며, 필요하면 프로젝트에 별도로 설치합니다.
Unity 내장 생성기는 기본 11개 강체 구성을 사용합니다. 별도 손·발 강체 추가 옵션은 Bz 사용 시에만 활성화됩니다.

## 패키지 업데이트

절단 패키지의 Git URL을 위의 1.1.1 태그로 업데이트합니다.
프로젝트에서 별도로 설치한 래그돌 생성기는 계속 사용할 수 있습니다.

## 절단 콜라이더

- 절단면과 교차하지 않는 콜라이더는 원본 종류와 설정을 보존합니다.
- 로컬 축과 일치하는 절단은 BoxCollider의 중심과 크기만 줄입니다.
- 실제로 비스듬하게 잘린 Box와 잘린 Sphere·Capsule만 Convex MeshCollider로 변환합니다.
- 기존 MeshCollider는 원본 메시와 설정을 유지합니다.

1.1.1 수정은 이후 생성하는 출력에 적용됩니다. 이미 생성한 출력은 같은 절단 설정으로 다시 생성해야 합니다.

## 구성과 개발

- `Packages/com.mesh-tools.skinned-mesh-cutter/Editor`: 절단, 캡 UV, 마스크 편집, 래그돌 출력.
- `Packages/com.mesh-tools.skinned-mesh-cutter/Tests/Editor`: 외부 모델 없는 공용 회귀 테스트.
- 게임 전용 편집 창 확장, 씬, 모델 및 생성 에셋은 각 게임 프로젝트에서 관리합니다.

이 저장소를 수정하고 Unity 2022.3과 Unity 6에서 검증한 뒤 새 태그를 배포합니다.
기존 태그를 이동하지 않고, 프로젝트별 `manifest.json`과 `packages-lock.json`을 함께 커밋합니다.
설치된 `Library/PackageCache` 사본을 직접 수정하지 않습니다.
